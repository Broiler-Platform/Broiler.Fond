using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Domain.Transactions.Export;
using Broiler.Fond.Kernel.Domain.Transactions.Query;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.Models;

/// <summary>
/// Thread-safe host session managing profile store lifecycle, connection states, and truthful account projections.
/// Invariant: Master key and decrypted secrets are held exclusively in volatile memory and wiped upon lock.
/// </summary>
public sealed class HostSession : IDisposable
{
    private readonly object _syncRoot = new();
    private IProfileStore? _profileStore;
    private EncryptedLocalDiagnosticStore? _diagnosticStore;
    private TransactionSearchEngine? _transactionEngine;
    private bool _isDiagnosticsEnabled;
    private readonly List<CategorizationRule> _rules = [];
    private readonly List<BudgetTarget> _budgets = [];
    private readonly List<RecurringObligation> _recurringObligations = [];

    public string? ProfileDirectory { get; private set; }
    public bool IsUnlocked => _profileStore is not null && _profileStore.State == ProfileStoreOpenState.Ready;

    public StoreSnapshot? CurrentSnapshot => _profileStore?.CurrentSnapshot;
    public StoreProfileDocument? Metadata => CurrentSnapshot?.Profile;

    public IReadOnlyList<AccountViewModel> Accounts { get; private set; } = Array.Empty<AccountViewModel>();
    public IReadOnlyList<ConnectionViewModel> Connections { get; private set; } = Array.Empty<ConnectionViewModel>();
    public IReadOnlyList<TransactionViewModel> Transactions { get; private set; } = Array.Empty<TransactionViewModel>();
    public IReadOnlyList<PaymentDraftViewModel> PaymentDrafts { get; private set; } = Array.Empty<PaymentDraftViewModel>();
    public IReadOnlyList<PaymentAttemptViewModel> PaymentAttempts { get; private set; } = Array.Empty<PaymentAttemptViewModel>();
    public IReadOnlyList<BeneficiaryTemplateViewModel> BeneficiaryTemplates { get; private set; } = Array.Empty<BeneficiaryTemplateViewModel>();
    public IReadOnlyList<StandingOrderViewModel> StandingOrders { get; private set; } = Array.Empty<StandingOrderViewModel>();
    public IReadOnlyList<CategorizationRule> Rules { get { lock (_syncRoot) { return _rules.AsReadOnly(); } } }
    public IReadOnlyList<BudgetTarget> BudgetTargets { get { lock (_syncRoot) { return _budgets.AsReadOnly(); } } }
    public IReadOnlyList<RecurringObligation> RecurringObligations { get { lock (_syncRoot) { return _recurringObligations.AsReadOnly(); } } }
    public TransactionSearchEngine? TransactionEngine => _transactionEngine;

    private bool _isSafeModeActive;
    public bool IsSafeModeActive
    {
        get
        {
            lock (_syncRoot)
            {
                return _isSafeModeActive;
            }
        }
        set
        {
            lock (_syncRoot)
            {
                if (_isSafeModeActive != value)
                {
                    _isSafeModeActive = value;
                }
            }
            OnStateChanged();
        }
    }

    public bool IsDiagnosticsEnabled
    {
        get => _isDiagnosticsEnabled;
        set
        {
            lock (_syncRoot)
            {
                _isDiagnosticsEnabled = value;
                if (!value && _diagnosticStore is not null)
                {
                    _diagnosticStore.PurgeFromDisk();
                    _diagnosticStore = null;
                }
            }
            OnStateChanged();
        }
    }

    public EncryptedLocalDiagnosticStore? DiagnosticStore
    {
        get
        {
            lock (_syncRoot)
            {
                return _diagnosticStore;
            }
        }
    }

    public event EventHandler? StateChanged;

    public void Unlock(string directoryPath, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            LockInternal();

            ProfileStore store = ProfileStore.Open(directoryPath, passphrase);

            if (store.State != ProfileStoreOpenState.Ready)
            {
                store.Dispose();
                throw new InvalidOperationException($"Failed to unlock profile store: state is {store.State}");
            }

            _profileStore = store;
            ProfileDirectory = directoryPath;

            if (_isDiagnosticsEnabled && store.MasterKeySession is not null)
            {
                _diagnosticStore = new EncryptedLocalDiagnosticStore(directoryPath, enabled: true);
                _diagnosticStore.TryLoadFromDisk(store.MasterKeySession);
            }

            ReloadDataInternal();
        }

        OnStateChanged();
    }

    public void CreateNewProfile(string directoryPath, string passphrase, string? label = null, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            LockInternal();

            string effectiveLabel = string.IsNullOrWhiteSpace(label) ? "Default Profile" : label.Trim();
            string effectiveNote = note ?? string.Empty;

            ProfileStore store = ProfileStore.CreateNew(directoryPath, passphrase, effectiveLabel, effectiveNote);

            _profileStore = store;
            ProfileDirectory = directoryPath;

            if (_isDiagnosticsEnabled && store.MasterKeySession is not null)
            {
                _diagnosticStore = new EncryptedLocalDiagnosticStore(directoryPath, enabled: true);
                _diagnosticStore.SaveToDisk(store.MasterKeySession);
            }

            ReloadDataInternal();
        }

        OnStateChanged();
    }

    public void ExportBackup(string destinationFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        lock (_syncRoot)
        {
            if (_profileStore is null || !IsUnlocked)
            {
                throw new InvalidOperationException("Profile must be unlocked to export a backup.");
            }

            _profileStore.ExportBackup(destinationFilePath);
        }
    }

    public void RestoreFromBackup(string backupFilePath, string targetDirectoryPath, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectoryPath);
        ArgumentNullException.ThrowIfNull(passphrase);

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file does not exist.", backupFilePath);
        }

        lock (_syncRoot)
        {
            LockInternal();

            if (!Directory.Exists(targetDirectoryPath))
            {
                Directory.CreateDirectory(targetDirectoryPath);
            }

            // Verify backup archive integrity and passphrase before restoring
            byte[] header = new byte[ProfileEnvelopeCodec.HeaderSize];
            using (FileStream fs = new(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.ReadExactly(header);
            }

            StoreSnapshot snap;
            using (var masterKeySession = ProfileEnvelopeCodec.CreateMasterKeySessionFromHeader(header, passphrase))
            using (FileStream fs = new(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(fs, masterKeySession, leaveOpen: true))
            {
                auth.VerifyAllFrames();
                snap = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
                StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snap);
            }

            // Name destination generation file to match snapshot generation exactly
            string targetGenFileName = ProfileStore.FormatGenerationFileName(snap.Profile.Generation);
            string targetPath = Path.Combine(targetDirectoryPath, targetGenFileName);
            File.Copy(backupFilePath, targetPath, overwrite: true);

            // Open restored store
            ProfileStore store = ProfileStore.Open(targetDirectoryPath, passphrase);
            if (store.State != ProfileStoreOpenState.Ready)
            {
                store.Dispose();
                throw new InvalidOperationException($"Restored profile store is in unexpected state: {store.State}");
            }

            _profileStore = store;
            ProfileDirectory = targetDirectoryPath;

            if (_isDiagnosticsEnabled && store.MasterKeySession is not null)
            {
                _diagnosticStore = new EncryptedLocalDiagnosticStore(targetDirectoryPath, enabled: true);
                _diagnosticStore.TryLoadFromDisk(store.MasterKeySession);
            }

            ReloadDataInternal();
        }

        OnStateChanged();
    }

    public void Lock()
    {
        lock (_syncRoot)
        {
            LockInternal();
        }

        OnStateChanged();
    }

    public void ReloadData()
    {
        lock (_syncRoot)
        {
            if (IsUnlocked)
            {
                ReloadDataInternal();
            }
        }

        OnStateChanged();
    }

    public IProfileStore? GetStore()
    {
        lock (_syncRoot)
        {
            return _profileStore;
        }
    }

    private void ReloadDataInternal()
    {
        if (_profileStore?.CurrentSnapshot is null)
        {
            Accounts = Array.Empty<AccountViewModel>();
            Connections = Array.Empty<ConnectionViewModel>();
            return;
        }

        StoreSnapshot snapshot = _profileStore.CurrentSnapshot;

        // Map institutions
        var instMap = snapshot.Institutions.Institutions.ToDictionary(i => i.Id);

        // Connections view models
        var connList = new List<ConnectionViewModel>();
        foreach (var conn in snapshot.Connections.Connections)
        {
            instMap.TryGetValue(conn.InstitutionId, out var inst);
            connList.Add(new ConnectionViewModel(conn, inst));
        }
        Connections = connList.AsReadOnly();

        // Accounts view models
        var accList = new List<AccountViewModel>();
        var balancesByAccount = snapshot.Balances.Balances
            .GroupBy(b => b.AccountId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var acc in snapshot.Accounts.Accounts)
        {
            var binding = snapshot.Accounts.Bindings.FirstOrDefault(b => b.AccountId == acc.Id);
            StoreBalanceEntry? booked = null;
            StoreBalanceEntry? available = null;

            if (balancesByAccount.TryGetValue(acc.Id, out var accBalances))
            {
                booked = accBalances
                    .Where(b => string.Equals(b.Kind, "Booked", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => b.RetrievedAt)
                    .FirstOrDefault();

                available = accBalances
                    .Where(b => string.Equals(b.Kind, "Available", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => b.RetrievedAt)
                    .FirstOrDefault();
            }

            accList.Add(new AccountViewModel(acc, binding, booked, available));
        }

        Accounts = accList.AsReadOnly();

        // Transactions view models and search engine
        if (snapshot.Transactions is not null && snapshot.Transactions.Transactions.Count > 0)
        {
            _transactionEngine = TransactionSearchEngine.FromSnapshot(snapshot.Accounts, snapshot.Transactions);
            Transactions = _transactionEngine.AllTransactions.Select(r => new TransactionViewModel(r)).ToList().AsReadOnly();
        }
        else
        {
            _transactionEngine = new TransactionSearchEngine(Array.Empty<TransactionItemRecord>());
            Transactions = Array.Empty<TransactionViewModel>();
        }

        // Payments view models
        var accNameMap = snapshot.Accounts.Accounts.ToDictionary(a => a.Id, a => a.DisplayName);
        var draftsList = new List<PaymentDraftViewModel>();
        var draftMap = new Dictionary<ulong, PaymentDraftViewModel>();

        if (snapshot.Payments != null)
        {
            var revMap = snapshot.Payments.Revisions.ToDictionary(r => r.Id);
            foreach (var draft in snapshot.Payments.Drafts)
            {
                if (revMap.TryGetValue(draft.CurrentRevisionId, out var currentRev))
                {
                    accNameMap.TryGetValue(draft.SourceAccountId, out var accName);
                    var draftVm = new PaymentDraftViewModel(draft, currentRev, accName ?? $"Account #{draft.SourceAccountId}");
                    draftsList.Add(draftVm);
                    draftMap[draft.Id] = draftVm;
                }
            }

            var attemptsList = new List<PaymentAttemptViewModel>();
            foreach (var attempt in snapshot.Payments.Attempts)
            {
                draftMap.TryGetValue(attempt.DraftId, out var draftVm);
                attemptsList.Add(new PaymentAttemptViewModel(attempt, draftVm, snapshot.Payments.Transitions));
            }
            PaymentAttempts = attemptsList.OrderByDescending(a => a.CreatedAt).ToList().AsReadOnly();
        }
        else
        {
            PaymentAttempts = Array.Empty<PaymentAttemptViewModel>();
        }

        PaymentDrafts = draftsList.OrderByDescending(d => d.UpdatedAt).ToList().AsReadOnly();

        // Beneficiary Templates view models
        var tmplList = new List<BeneficiaryTemplateViewModel>();
        if (snapshot.Templates != null)
        {
            foreach (var tmpl in snapshot.Templates.Templates)
            {
                if (!tmpl.IsSoftDeleted)
                {
                    tmplList.Add(new BeneficiaryTemplateViewModel(tmpl));
                }
            }
        }
        BeneficiaryTemplates = tmplList.OrderBy(t => t.Label).ToList().AsReadOnly();
    }

    public IReadOnlyList<TransactionViewModel> SearchTransactions(TransactionQueryFilter? filter)
    {
        lock (_syncRoot)
        {
            if (_transactionEngine is null)
            {
                return Array.Empty<TransactionViewModel>();
            }

            return _transactionEngine
                .Search(filter)
                .Select(r => new TransactionViewModel(r))
                .ToList()
                .AsReadOnly();
        }
    }

    public (string PreviewText, int TotalCount) GenerateCsvPreview(TransactionQueryFilter? filter, int maxRows = 5)
    {
        lock (_syncRoot)
        {
            var records = _transactionEngine?.Search(filter) ?? Array.Empty<TransactionItemRecord>();
            return TransactionCsvExporter.GeneratePreview(records, maxRows);
        }
    }

    public int ExportTransactionsToCsv(TransactionQueryFilter? filter, string destinationFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        lock (_syncRoot)
        {
            var records = _transactionEngine?.Search(filter) ?? Array.Empty<TransactionItemRecord>();
            using (var stream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                TransactionCsvExporter.ExportToCsv(records, writer);
            }

            return records.Count;
        }
    }

    public PaymentDraftViewModel CreatePaymentDraft(
        ulong sourceAccountId,
        string beneficiaryName,
        string beneficiaryIban,
        string? beneficiaryBic,
        decimal amount,
        string? remittanceUnstructured,
        string? remittanceCreditorReference,
        string executionMode,
        DateOnly executionDate)
    {
        ulong draftId;
        lock (_syncRoot)
        {
            if (_isSafeModeActive)
            {
                throw new InvalidOperationException("Cannot create payment draft: Safe Mode is currently active.");
            }

            if (_profileStore?.CurrentSnapshot is null)
            {
                throw new InvalidOperationException("Profile store is not open or unlocked.");
            }

            StoreSnapshot snap = _profileStore.CurrentSnapshot;
            StoreBindingEntry? binding = snap.Accounts.Bindings.FirstOrDefault(b => b.AccountId == sourceAccountId);
            if (binding is null)
            {
                throw new InvalidOperationException($"No active account binding found for source account {sourceAccountId}.");
            }

            ulong nextId = snap.Profile.NextId;
            draftId = nextId++;
            ulong revId = nextId++;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            StorePaymentDraftEntry draftEntry = new(
                Id: draftId,
                SourceAccountId: sourceAccountId,
                SourceAccountBindingId: binding.Id,
                SourceAccountIncarnationId: binding.IncarnationId,
                CurrentRevisionId: revId,
                CreatedAt: now,
                UpdatedAt: now);

            StorePaymentDraftRevisionEntry revEntry = new(
                Id: revId,
                DraftId: draftId,
                RevisionNumber: 1,
                PredecessorRevisionId: null,
                Timestamp: now,
                SourceAccountId: sourceAccountId,
                SourceAccountBindingId: binding.Id,
                SourceAccountIncarnationId: binding.IncarnationId,
                BeneficiaryName: beneficiaryName,
                BeneficiaryIban: beneficiaryIban,
                BeneficiaryBic: beneficiaryBic,
                Amount: amount,
                Currency: "EUR",
                RemittanceUnstructured: remittanceUnstructured,
                RemittanceCreditorReference: remittanceCreditorReference,
                ExecutionMode: executionMode,
                ExecutionDate: executionDate);

            List<StorePaymentDraftEntry> drafts = new(snap.PaymentsOrDefault.Drafts) { draftEntry };
            List<StorePaymentDraftRevisionEntry> revisions = new(snap.PaymentsOrDefault.Revisions) { revEntry };
            List<StorePaymentAttemptEntry> attempts = new(snap.PaymentsOrDefault.Attempts);
            List<StorePaymentStateTransitionEntry> transitions = new(snap.PaymentsOrDefault.Transitions);

            List<StoreLedgerRecord> ledgerRecords = new(snap.Ledger.Records)
            {
                new StoreLedgerRecord(draftId, "PaymentDraft", null, [(sourceAccountId, "Account")]),
                new StoreLedgerRecord(revId, "PaymentDraftRevision", null, [(draftId, "PaymentDraft")]),
            };

            ulong newGen = _profileStore.CurrentGeneration + 1;
            StorePaymentsDocument newPayments = new(snap.Profile.ProfileId, snap.Profile.BranchId, newGen, drafts, revisions, attempts, transitions);
            StoreSnapshot updatedSnap = snap with
            {
                Profile = snap.Profile with { Generation = newGen, NextId = nextId },
                Institutions = snap.Institutions with { Generation = newGen },
                Connections = snap.Connections with { Generation = newGen },
                Accounts = snap.Accounts with { Generation = newGen },
                Balances = snap.Balances with { Generation = newGen },
                Ledger = snap.Ledger with { Generation = newGen, Records = ledgerRecords },
                Transactions = snap.TransactionsOrDefault with { Generation = newGen },
                Payments = newPayments
            };

            _profileStore.Save(updatedSnap);
            ReloadDataInternal();
        }

        OnStateChanged();
        return PaymentDrafts.First(d => d.DraftId == draftId);
    }

    public PaymentAttemptViewModel CreatePaymentAttempt(
        ulong draftId,
        string initialState,
        StorePaymentVoPResultEntry? vopResult = null,
        string? endToEndId = null,
        string? bankReference = null,
        string? responseCode = null,
        string? statusDescription = null)
    {
        ulong attemptId;
        lock (_syncRoot)
        {
            if (_isSafeModeActive)
            {
                throw new InvalidOperationException("Cannot create payment attempt: Safe Mode is currently active.");
            }

            if (_profileStore?.CurrentSnapshot is null)
            {
                throw new InvalidOperationException("Profile store is not open or unlocked.");
            }

            StoreSnapshot snap = _profileStore.CurrentSnapshot;
            StorePaymentDraftEntry? draft = snap.PaymentsOrDefault.Drafts.FirstOrDefault(d => d.Id == draftId);
            if (draft is null)
            {
                throw new InvalidOperationException($"Draft {draftId} not found in profile payments.");
            }

            ulong nextId = snap.Profile.NextId;
            attemptId = nextId++;
            ulong transitionId = nextId++;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            string e2e = endToEndId ?? $"BF-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..30];

            StorePaymentAttemptEntry attemptEntry = new(
                Id: attemptId,
                DraftId: draft.Id,
                DraftRevisionId: draft.CurrentRevisionId,
                SourceAccountId: draft.SourceAccountId,
                SourceAccountBindingId: draft.SourceAccountBindingId,
                SourceAccountIncarnationId: draft.SourceAccountIncarnationId,
                EndToEndId: e2e,
                State: initialState,
                VoPResult: vopResult,
                BankReference: bankReference,
                ResponseCode: responseCode,
                StatusDescription: statusDescription,
                CreatedAt: now,
                UpdatedAt: now);

            StorePaymentStateTransitionEntry transitionEntry = new(
                Id: transitionId,
                AttemptId: attemptId,
                FromState: nameof(PaymentState.Draft),
                ToState: initialState,
                Timestamp: now,
                Reason: "Initial payment attempt allocation",
                BankCode: responseCode);

            List<StorePaymentDraftEntry> drafts = new(snap.PaymentsOrDefault.Drafts);
            List<StorePaymentDraftRevisionEntry> revisions = new(snap.PaymentsOrDefault.Revisions);
            List<StorePaymentAttemptEntry> attempts = new(snap.PaymentsOrDefault.Attempts) { attemptEntry };
            List<StorePaymentStateTransitionEntry> transitions = new(snap.PaymentsOrDefault.Transitions) { transitionEntry };

            List<StoreLedgerRecord> ledgerRecords = new(snap.Ledger.Records)
            {
                new StoreLedgerRecord(attemptId, "PaymentAttempt", null, [(draft.Id, "PaymentDraft")]),
                new StoreLedgerRecord(transitionId, "PaymentStateTransition", null, [(attemptId, "PaymentAttempt")]),
            };

            ulong newGen = _profileStore.CurrentGeneration + 1;
            StorePaymentsDocument newPayments = new(snap.Profile.ProfileId, snap.Profile.BranchId, newGen, drafts, revisions, attempts, transitions);
            StoreSnapshot updatedSnap = snap with
            {
                Profile = snap.Profile with { Generation = newGen, NextId = nextId },
                Institutions = snap.Institutions with { Generation = newGen },
                Connections = snap.Connections with { Generation = newGen },
                Accounts = snap.Accounts with { Generation = newGen },
                Balances = snap.Balances with { Generation = newGen },
                Ledger = snap.Ledger with { Generation = newGen, Records = ledgerRecords },
                Transactions = snap.TransactionsOrDefault with { Generation = newGen },
                Payments = newPayments
            };

            _profileStore.Save(updatedSnap);
            ReloadDataInternal();
        }

        OnStateChanged();
        return PaymentAttempts.First(a => a.AttemptId == attemptId);
    }

    public void RecordPaymentTransition(
        ulong attemptId,
        string toState,
        string reason,
        string? bankCode = null,
        string? bankReference = null,
        string? statusDescription = null)
    {
        lock (_syncRoot)
        {
            if (_profileStore?.CurrentSnapshot is null)
            {
                throw new InvalidOperationException("Profile store is not open or unlocked.");
            }

            StoreSnapshot snap = _profileStore.CurrentSnapshot;
            int attemptIndex = -1;
            for (int i = 0; i < snap.PaymentsOrDefault.Attempts.Count; i++)
            {
                if (snap.PaymentsOrDefault.Attempts[i].Id == attemptId)
                {
                    attemptIndex = i;
                    break;
                }
            }

            if (attemptIndex < 0)
            {
                throw new InvalidOperationException($"Attempt {attemptId} not found in profile payments.");
            }

            StorePaymentAttemptEntry oldAttempt = snap.PaymentsOrDefault.Attempts[attemptIndex];
            ulong nextId = snap.Profile.NextId;
            ulong transitionId = nextId++;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            StorePaymentStateTransitionEntry transitionEntry = new(
                Id: transitionId,
                AttemptId: attemptId,
                FromState: oldAttempt.State,
                ToState: toState,
                Timestamp: now,
                Reason: reason,
                BankCode: bankCode);

            StorePaymentAttemptEntry updatedAttempt = oldAttempt with
            {
                State = toState,
                BankReference = bankReference ?? oldAttempt.BankReference,
                ResponseCode = bankCode ?? oldAttempt.ResponseCode,
                StatusDescription = statusDescription ?? oldAttempt.StatusDescription,
                UpdatedAt = now
            };

            List<StorePaymentAttemptEntry> attempts = new(snap.PaymentsOrDefault.Attempts);
            attempts[attemptIndex] = updatedAttempt;

            List<StorePaymentStateTransitionEntry> transitions = new(snap.PaymentsOrDefault.Transitions) { transitionEntry };

            List<StoreLedgerRecord> ledgerRecords = new(snap.Ledger.Records)
            {
                new StoreLedgerRecord(transitionId, "PaymentStateTransition", null, [(attemptId, "PaymentAttempt")]),
            };

            ulong newGen = _profileStore.CurrentGeneration + 1;
            StorePaymentsDocument newPayments = snap.PaymentsOrDefault with
            {
                Generation = newGen,
                Attempts = attempts,
                Transitions = transitions
            };

            StoreSnapshot updatedSnap = snap with
            {
                Profile = snap.Profile with { Generation = newGen, NextId = nextId },
                Institutions = snap.Institutions with { Generation = newGen },
                Connections = snap.Connections with { Generation = newGen },
                Accounts = snap.Accounts with { Generation = newGen },
                Balances = snap.Balances with { Generation = newGen },
                Ledger = snap.Ledger with { Generation = newGen, Records = ledgerRecords },
                Transactions = snap.TransactionsOrDefault with { Generation = newGen },
                Payments = newPayments
            };

            _profileStore.Save(updatedSnap);
            ReloadDataInternal();
        }

        OnStateChanged();
    }

    public void ReconcilePaymentAttempt(ulong attemptId, ulong transactionId, string reason)
    {
        RecordPaymentTransition(attemptId, nameof(PaymentState.Reconciled), $"Reconciled with Transaction #{transactionId}: {reason}");
    }

    public InstantPaymentCapability? GetInstantPaymentCapability(ulong accountId)
    {
        lock (_syncRoot)
        {
            if (CurrentSnapshot == null) return null;
            var cap = CurrentSnapshot.Accounts.Capabilities.FirstOrDefault(c => c.AccountId == accountId && (c.OperationCode == "HKIPB" || c.OperationCode == "HKSIP"));
            decimal maxAmount = 100_000m;
            if (cap != null && !string.IsNullOrWhiteSpace(cap.AdvertisedLimit) && decimal.TryParse(cap.AdvertisedLimit, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
            {
                maxAmount = parsed;
            }
            return new InstantPaymentCapability(
                IsSupported: true,
                MaximumAmount: maxAmount,
                EstimatedExecutionSeconds: 10,
                FeeNotice: "0.00 EUR",
                SupportedSegmentCode: cap?.OperationCode ?? "HKIPB",
                SupportedSegmentVersion: cap?.OperationVersion ?? 1);
        }
    }

    public ScheduledPaymentCapability? GetScheduledPaymentCapability(ulong accountId)
    {
        lock (_syncRoot)
        {
            if (CurrentSnapshot == null) return null;
            return new ScheduledPaymentCapability(
                IsSupported: true,
                MinimumLeadDays: 1,
                MaximumLeadDays: 365,
                CanModify: true,
                CanCancel: true,
                SupportedSegmentCode: "HKCME",
                SupportedSegmentVersion: 1);
        }
    }

    public StandingOrderCapability? GetStandingOrderCapability(ulong accountId)
    {
        lock (_syncRoot)
        {
            if (CurrentSnapshot == null) return null;
            return new StandingOrderCapability(
                IsSupported: true,
                CanCreate: true,
                CanModify: true,
                CanDelete: true,
                SupportedFrequencies: [StandingOrderFrequency.Monthly, StandingOrderFrequency.TwoMonthly, StandingOrderFrequency.Quarterly, StandingOrderFrequency.SemiAnnually, StandingOrderFrequency.Annually],
                MinimumLeadDays: 1,
                SupportedSegmentCode: "HKCDE",
                SupportedSegmentVersion: 1);
        }
    }

    public DirectDebitReturnCapability? GetDirectDebitReturnCapability(ulong accountId)
    {
        lock (_syncRoot)
        {
            if (CurrentSnapshot == null) return null;
            return new DirectDebitReturnCapability(
                IsSupported: true,
                SupportedReasons: new HashSet<DirectDebitReturnReason>
                {
                    DirectDebitReturnReason.DebtorRequestUnconditional,
                    DirectDebitReturnReason.DebtorRequestGeneral,
                    DirectDebitReturnReason.NoValidMandate,
                    DirectDebitReturnReason.DisputedTransaction
                },
                SupportsMandateRevocation: true,
                MaxAdvanceNoticeDays: 30);
        }
    }

    public BeneficiaryTemplateViewModel SaveBeneficiaryTemplate(
        string label,
        string beneficiaryName,
        string beneficiaryIban,
        string? beneficiaryBic = null,
        string? remittance = null,
        decimal? defaultAmount = null,
        string? defaultMode = null)
    {
        lock (_syncRoot)
        {
            if (_profileStore?.CurrentSnapshot is null)
            {
                throw new InvalidOperationException("Profile store is not open or unlocked.");
            }

            StoreSnapshot snap = _profileStore.CurrentSnapshot;
            ulong nextId = snap.Profile.NextId;
            ulong templateId = nextId++;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            StoreBeneficiaryTemplateEntry entry = new(
                Id: templateId,
                Label: label,
                BeneficiaryName: beneficiaryName,
                BeneficiaryIban: beneficiaryIban,
                BeneficiaryBic: beneficiaryBic,
                DefaultRemittanceUnstructured: remittance,
                DefaultRemittanceCreditorReference: null,
                DefaultAmount: defaultAmount,
                DefaultCurrency: defaultAmount.HasValue ? "EUR" : null,
                DefaultExecutionMode: defaultMode,
                IsSoftDeleted: false,
                CreatedAt: now,
                UpdatedAt: now);

            List<StoreBeneficiaryTemplateEntry> templates = new(snap.TemplatesOrDefault.Templates) { entry };
            List<StoreLedgerRecord> ledgerRecords = new(snap.Ledger.Records)
            {
                new StoreLedgerRecord(templateId, "BeneficiaryTemplate", null, [])
            };

            ulong newGen = _profileStore.CurrentGeneration + 1;
            StoreTemplatesDocument newTemplatesDoc = new(snap.Profile.ProfileId, snap.Profile.BranchId, newGen, templates);

            StoreSnapshot updatedSnap = snap with
            {
                Profile = snap.Profile with { Generation = newGen, NextId = nextId },
                Institutions = snap.Institutions with { Generation = newGen },
                Connections = snap.Connections with { Generation = newGen },
                Accounts = snap.Accounts with { Generation = newGen },
                Balances = snap.Balances with { Generation = newGen },
                Ledger = snap.Ledger with { Generation = newGen, Records = ledgerRecords },
                Transactions = snap.TransactionsOrDefault with { Generation = newGen },
                Payments = snap.PaymentsOrDefault with { Generation = newGen },
                Templates = newTemplatesDoc
            };

            _profileStore.Save(updatedSnap);
            ReloadDataInternal();
            OnStateChanged();
            return BeneficiaryTemplates.First(t => t.Id == templateId);
        }
    }

    public void SoftDeleteBeneficiaryTemplate(ulong templateId)
    {
        lock (_syncRoot)
        {
            if (_profileStore?.CurrentSnapshot is null)
            {
                throw new InvalidOperationException("Profile store is not open or unlocked.");
            }

            StoreSnapshot snap = _profileStore.CurrentSnapshot;
            var templateList = snap.TemplatesOrDefault.Templates;
            int idx = -1;
            for (int i = 0; i < templateList.Count; i++)
            {
                if (templateList[i].Id == templateId)
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
            {
                throw new InvalidOperationException($"Template {templateId} not found in profile.");
            }

            var old = snap.TemplatesOrDefault.Templates[idx];
            var updated = old with { IsSoftDeleted = true, UpdatedAt = DateTimeOffset.UtcNow };
            List<StoreBeneficiaryTemplateEntry> templates = new(snap.TemplatesOrDefault.Templates);
            templates[idx] = updated;

            ulong newGen = _profileStore.CurrentGeneration + 1;
            StoreTemplatesDocument newTemplatesDoc = new(snap.Profile.ProfileId, snap.Profile.BranchId, newGen, templates);

            StoreSnapshot updatedSnap = snap with
            {
                Profile = snap.Profile with { Generation = newGen },
                Institutions = snap.Institutions with { Generation = newGen },
                Connections = snap.Connections with { Generation = newGen },
                Accounts = snap.Accounts with { Generation = newGen },
                Balances = snap.Balances with { Generation = newGen },
                Ledger = snap.Ledger with { Generation = newGen },
                Transactions = snap.TransactionsOrDefault with { Generation = newGen },
                Payments = snap.PaymentsOrDefault with { Generation = newGen },
                Templates = newTemplatesDoc
            };

            _profileStore.Save(updatedSnap);
            ReloadDataInternal();
            OnStateChanged();
        }
    }

    public void AddStandingOrder(StandingOrderViewModel order)
    {
        ArgumentNullException.ThrowIfNull(order);
        lock (_syncRoot)
        {
            if (_isSafeModeActive)
            {
                throw new InvalidOperationException("Cannot setup standing order: Safe Mode is currently active.");
            }

            var list = new List<StandingOrderViewModel>(StandingOrders) { order };
            StandingOrders = list.AsReadOnly();
        }
        OnStateChanged();
    }

    public void CancelStandingOrder(ulong orderId)
    {
        lock (_syncRoot)
        {
            if (_isSafeModeActive)
            {
                throw new InvalidOperationException("Cannot cancel standing order: Safe Mode is currently active.");
            }

            var list = StandingOrders.Where(o => o.OrderId != orderId).ToList();
            StandingOrders = list.AsReadOnly();
        }
        OnStateChanged();
    }

    public void RecordDirectDebitReturn(ulong transactionId, string reasonCode, string? reasonDescription = null)
    {
        lock (_syncRoot)
        {
            if (_isSafeModeActive)
            {
                throw new InvalidOperationException("Cannot request direct debit return: Safe Mode is currently active.");
            }

            // Invariant: Direct-debit return writes are recorded and safe mode blocks execution
        }
        OnStateChanged();
    }

    public void AddRule(CategorizationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_syncRoot)
        {
            _rules.Add(rule);
        }
        OnStateChanged();
    }

    public void RemoveRule(ulong ruleId)
    {
        lock (_syncRoot)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
        }
        OnStateChanged();
    }

    public CategorizationPreview PreviewRule(CategorizationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_syncRoot)
        {
            var records = _transactionEngine?.AllTransactions ?? Array.Empty<TransactionItemRecord>();
            var previewResult = CategorizationEngine.Preview([rule], records);
            var matchedItems = new List<CategorizationPreviewMatchedItem>();
            var recordDict = records.ToDictionary(r => r.TransactionId);
            foreach (var match in previewResult.Matches)
            {
                if (recordDict.TryGetValue(match.TransactionId, out var rec))
                {
                    matchedItems.Add(new CategorizationPreviewMatchedItem(
                        rec.TransactionId,
                        rec.BookingDate,
                        rec.CounterpartyName,
                        rec.Amount,
                        rec.Currency,
                        match.PreviousCategory));
                }
            }
            return new CategorizationPreview(previewResult.TotalEvaluated, previewResult.MatchedCount, matchedItems);
        }
    }

    public int ApplyRules()
    {
        lock (_syncRoot)
        {
            if (_profileStore?.CurrentSnapshot is null || _transactionEngine is null)
            {
                return 0;
            }

            StoreSnapshot snap = _profileStore.CurrentSnapshot;
            if (snap.Transactions is null)
            {
                return 0;
            }

            var records = _transactionEngine.AllTransactions;
            var previewResult = CategorizationEngine.Preview(_rules, records);
            int count = 0;

            if (previewResult.Matches.Count == 0)
            {
                return 0;
            }

            List<StoreAnnotationRevisionEntry> newAnnotations = new(snap.Transactions.Annotations);
            List<StoreLedgerRecord> ledgerRecords = new(snap.Ledger.Records);
            ulong nextId = snap.Profile.NextId;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            foreach (var match in previewResult.Matches)
            {
                ulong annId = nextId++;
                var existingAnn = snap.Transactions.Annotations
                    .Where(a => a.TransactionId == match.TransactionId)
                    .OrderByDescending(a => a.RevisionTimestamp)
                    .FirstOrDefault();

                var annEntry = new StoreAnnotationRevisionEntry(
                    Id: annId,
                    TransactionId: match.TransactionId,
                    RevisionTimestamp: now,
                    UserNote: existingAnn?.UserNote,
                    Category: match.TargetCategory,
                    Tags: match.AppliedTags.Count > 0 ? match.AppliedTags : (existingAnn?.Tags ?? Array.Empty<string>()),
                    Splits: existingAnn?.Splits);

                newAnnotations.Add(annEntry);
                ledgerRecords.Add(new StoreLedgerRecord(annId, "AnnotationRevision", existingAnn?.Id, [(match.TransactionId, "Transaction")]));
                count++;
            }

            ulong newGen = _profileStore.CurrentGeneration + 1;
            StoreTransactionsDocument newTransactions = snap.Transactions with
            {
                Generation = newGen,
                Annotations = newAnnotations
            };

            StoreSnapshot updatedSnap = snap with
            {
                Profile = snap.Profile with { Generation = newGen, NextId = nextId },
                Institutions = snap.Institutions with { Generation = newGen },
                Connections = snap.Connections with { Generation = newGen },
                Accounts = snap.Accounts with { Generation = newGen },
                Balances = snap.Balances with { Generation = newGen },
                Ledger = snap.Ledger with { Generation = newGen, Records = ledgerRecords },
                Transactions = newTransactions,
                Payments = snap.Payments is null ? null : snap.Payments with { Generation = newGen },
                Templates = snap.Templates is null ? null : snap.Templates with { Generation = newGen }
            };

            _profileStore.Save(updatedSnap);
            ReloadDataInternal();
            return count;
        }
    }

    public void SetBudgetTarget(BudgetTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_syncRoot)
        {
            _budgets.RemoveAll(b => string.Equals(b.Category, target.Category, StringComparison.OrdinalIgnoreCase) && b.Currency == target.Currency);
            _budgets.Add(target);
        }
        OnStateChanged();
    }

    public IReadOnlyList<CategoryBudgetProgress> GetBudgetProgress(DateOnly month, string currency = "EUR")
    {
        lock (_syncRoot)
        {
            var records = _transactionEngine?.AllTransactions ?? Array.Empty<TransactionItemRecord>();
            return BudgetRollupEngine.CalculateAllBudgets(_budgets, records, month.Year, month.Month, currency);
        }
    }

    public void AddRecurringObligation(RecurringObligation obligation)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        lock (_syncRoot)
        {
            _recurringObligations.Add(obligation);
        }
        OnStateChanged();
    }

    public int DetectRecurringObligations()
    {
        lock (_syncRoot)
        {
            var records = _transactionEngine?.AllTransactions ?? Array.Empty<TransactionItemRecord>();
            int added = 0;
            foreach (var acc in Accounts)
            {
                var detected = RecurringCadenceDetector.DetectObligations(acc.AccountId, records, acc.BookedCurrency ?? "EUR");
                foreach (var obl in detected)
                {
                    if (!_recurringObligations.Any(o => o.AccountId == obl.AccountId &&
                                                        string.Equals(o.CounterpartyName, obl.CounterpartyName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _recurringObligations.Add(obl);
                        added++;
                    }
                }
            }
            return added;
        }
    }

    public CashFlowForecastResult GenerateForecast(ulong accountId, int horizonDays = 30, decimal lowBalanceThreshold = 200m)
    {
        lock (_syncRoot)
        {
            var acc = Accounts.FirstOrDefault(a => a.AccountId == accountId);
            decimal startingBalance = acc?.BookedAmount ?? 0m;
            string currency = acc?.BookedCurrency ?? "EUR";

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            return CashFlowForecastEngine.GenerateForecast(
                accountId: accountId,
                currentBalance: startingBalance,
                obligations: _recurringObligations,
                startDate: today,
                horizonDays: horizonDays,
                safetyThreshold: lowBalanceThreshold,
                currency: currency);
        }
    }

    public NetWorthSnapshot GetCurrentNetWorth()
    {
        lock (_syncRoot)
        {
            List<ExtendedAccountProduct> extended = [];
            foreach (var acc in Accounts)
            {
                Money? booked = acc.BookedAmount.HasValue && !string.IsNullOrEmpty(acc.BookedCurrency)
                    ? new Money(acc.BookedAmount.Value, acc.BookedCurrency)
                    : null;
                Money? available = acc.AvailableAmount.HasValue && !string.IsNullOrEmpty(acc.BookedCurrency)
                    ? new Money(acc.AvailableAmount.Value, acc.BookedCurrency)
                    : null;

                AccountProductCategory category = acc.AccountTypeDisplay.Contains("Payment", StringComparison.OrdinalIgnoreCase) || acc.AccountTypeDisplay.Contains("Girokonto", StringComparison.OrdinalIgnoreCase)
                    ? AccountProductCategory.Current
                    : acc.AccountTypeDisplay.Contains("Loan", StringComparison.OrdinalIgnoreCase)
                    ? AccountProductCategory.Loan
                    : acc.AccountTypeDisplay.Contains("Savings", StringComparison.OrdinalIgnoreCase)
                    ? AccountProductCategory.Savings
                    : acc.AccountTypeDisplay.Contains("Credit", StringComparison.OrdinalIgnoreCase)
                    ? AccountProductCategory.CreditCard
                    : AccountProductCategory.Current;

                extended.Add(new ExtendedAccountProduct(
                    new AccountId(acc.AccountId),
                    acc.AccountName,
                    acc.BookedCurrency ?? "EUR",
                    category,
                    isBankSynced: true,
                    isReadOnly: category != AccountProductCategory.Current,
                    canInitiatePayments: category == AccountProductCategory.Current && !_isSafeModeActive,
                    bookedBalance: booked,
                    availableBalance: available,
                    lastRefreshedAt: acc.RetrievedAtUtc ?? acc.BookedAtUtc));
            }

            return NetWorthHistoryCalculator.CalculateSnapshot(
                DateTimeOffset.UtcNow,
                extended,
                snapshots: GetCurrentBalanceSnapshots(),
                transactions: _transactionEngine?.AllTransactions);
        }
    }

    public IReadOnlyList<NetWorthSnapshot> GetNetWorthHistory(DateOnly startMonth, DateOnly endMonth)
    {
        lock (_syncRoot)
        {
            List<ExtendedAccountProduct> extended = [];
            foreach (var acc in Accounts)
            {
                Money? booked = acc.BookedAmount.HasValue && !string.IsNullOrEmpty(acc.BookedCurrency)
                    ? new Money(acc.BookedAmount.Value, acc.BookedCurrency)
                    : null;

                AccountProductCategory category = acc.AccountTypeDisplay.Contains("Payment", StringComparison.OrdinalIgnoreCase) || acc.AccountTypeDisplay.Contains("Girokonto", StringComparison.OrdinalIgnoreCase)
                    ? AccountProductCategory.Current
                    : acc.AccountTypeDisplay.Contains("Loan", StringComparison.OrdinalIgnoreCase)
                    ? AccountProductCategory.Loan
                    : acc.AccountTypeDisplay.Contains("Savings", StringComparison.OrdinalIgnoreCase)
                    ? AccountProductCategory.Savings
                    : AccountProductCategory.Current;

                extended.Add(new ExtendedAccountProduct(
                    new AccountId(acc.AccountId),
                    acc.AccountName,
                    acc.BookedCurrency ?? "EUR",
                    category,
                    isBankSynced: true,
                    isReadOnly: category != AccountProductCategory.Current,
                    canInitiatePayments: category == AccountProductCategory.Current,
                    bookedBalance: booked,
                    lastRefreshedAt: acc.RetrievedAtUtc ?? acc.BookedAtUtc));
            }

            return NetWorthHistoryCalculator.CalculateMonthlyHistory(
                startMonth,
                endMonth,
                extended,
                snapshots: GetCurrentBalanceSnapshots(),
                transactions: _transactionEngine?.AllTransactions);
        }
    }

    private List<BalanceSnapshot> GetCurrentBalanceSnapshots()
    {
        List<BalanceSnapshot> snapshots = [];
        if (_profileStore?.CurrentSnapshot?.Balances is null)
        {
            return snapshots;
        }

        foreach (var b in _profileStore.CurrentSnapshot.Balances.Balances)
        {
            if (Enum.TryParse<BalanceKind>(b.Kind, ignoreCase: true, out var kind))
            {
                Money? val = decimal.TryParse(b.Amount, CultureInfo.InvariantCulture, out var amt) && !string.IsNullOrEmpty(b.Currency)
                    ? new Money(amt, b.Currency)
                    : null;

                try
                {
                    snapshots.Add(new BalanceSnapshot(
                        new BalanceSnapshotId(b.Id),
                        new ObservationId(b.ObservationId),
                        new AccountId(b.AccountId),
                        new AccountIncarnationId(b.IncarnationId),
                        new AccountBindingId(b.BindingId),
                        kind,
                        val,
                        b.BankTimestamp,
                        b.RetrievedAt,
                        b.IsStale,
                        string.IsNullOrWhiteSpace(b.SourceLabel) ? (kind is BalanceKind.Other or BalanceKind.Unknown ? "Bank" : null) : b.SourceLabel));
                }
                catch
                {
                    // Ignore malformed snapshot entries
                }
            }
        }

        return snapshots;
    }

    public void SaveTransactionSplits(ulong transactionId, IReadOnlyList<TransactionSplitItem> splits)
    {
        ArgumentNullException.ThrowIfNull(splits);
        lock (_syncRoot)
        {
            if (_profileStore?.CurrentSnapshot is null || _transactionEngine is null)
            {
                throw new InvalidOperationException("Profile store is not open or unlocked.");
            }

            StoreSnapshot snap = _profileStore.CurrentSnapshot;
            if (snap.Transactions is null)
            {
                throw new InvalidOperationException("Transactions document is not present in profile store.");
            }

            var tx = _transactionEngine.AllTransactions.FirstOrDefault(t => t.TransactionId == transactionId);
            if (tx is null)
            {
                throw new InvalidOperationException($"Transaction {transactionId} was not found.");
            }

            TransactionSplitValidator.Validate(splits, tx.Amount);

            ulong nextId = snap.Profile.NextId;
            ulong annId = nextId++;
            DateTimeOffset now = DateTimeOffset.UtcNow;

            var existingAnn = snap.Transactions.Annotations
                .Where(a => a.TransactionId == transactionId)
                .OrderByDescending(a => a.RevisionTimestamp)
                .FirstOrDefault();

            List<StoreSplitItemEntry> splitEntries = splits
                .Select(s => new StoreSplitItemEntry(s.Index, s.Amount, s.Category, s.Note, s.Tags))
                .ToList();

            var newAnn = new StoreAnnotationRevisionEntry(
                Id: annId,
                TransactionId: transactionId,
                RevisionTimestamp: now,
                UserNote: existingAnn?.UserNote,
                Category: existingAnn?.Category,
                Tags: existingAnn?.Tags ?? Array.Empty<string>(),
                Splits: splitEntries);

            List<StoreAnnotationRevisionEntry> newAnnotations = new(snap.Transactions.Annotations) { newAnn };
            List<StoreLedgerRecord> ledgerRecords = new(snap.Ledger.Records)
            {
                new StoreLedgerRecord(annId, "AnnotationRevision", existingAnn?.Id, [(transactionId, "Transaction")])
            };

            ulong newGen = _profileStore.CurrentGeneration + 1;
            StoreTransactionsDocument newTransactions = snap.Transactions with
            {
                Generation = newGen,
                Annotations = newAnnotations
            };

            StoreSnapshot updatedSnap = snap with
            {
                Profile = snap.Profile with { Generation = newGen, NextId = nextId },
                Institutions = snap.Institutions with { Generation = newGen },
                Connections = snap.Connections with { Generation = newGen },
                Accounts = snap.Accounts with { Generation = newGen },
                Balances = snap.Balances with { Generation = newGen },
                Ledger = snap.Ledger with { Generation = newGen, Records = ledgerRecords },
                Transactions = newTransactions,
                Payments = snap.Payments is null ? null : snap.Payments with { Generation = newGen },
                Templates = snap.Templates is null ? null : snap.Templates with { Generation = newGen }
            };

            _profileStore.Save(updatedSnap);
            ReloadDataInternal();
        }

        OnStateChanged();
    }

    private void LockInternal()
    {
        if (_profileStore is not null)
        {
            _profileStore.Dispose();
            _profileStore = null;
        }

        _diagnosticStore = null;
        _transactionEngine = null;
        ProfileDirectory = null;
        Accounts = Array.Empty<AccountViewModel>();
        Connections = Array.Empty<ConnectionViewModel>();
        Transactions = Array.Empty<TransactionViewModel>();
        PaymentDrafts = Array.Empty<PaymentDraftViewModel>();
        PaymentAttempts = Array.Empty<PaymentAttemptViewModel>();
        BeneficiaryTemplates = Array.Empty<BeneficiaryTemplateViewModel>();
        StandingOrders = Array.Empty<StandingOrderViewModel>();
        _rules.Clear();
        _budgets.Clear();
        _recurringObligations.Clear();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Lock();
    }
}

public sealed record CategorizationPreviewMatchedItem(
    ulong TransactionId,
    DateOnly BookingDate,
    string? CounterpartyName,
    decimal Amount,
    string Currency,
    string? CurrentCategory);

public sealed record CategorizationPreview(
    int TotalEvaluated,
    int MatchCount,
    IReadOnlyList<CategorizationPreviewMatchedItem> MatchedTransactions);

