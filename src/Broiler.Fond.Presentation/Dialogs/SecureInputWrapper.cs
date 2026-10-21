namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// In-memory ephemeral input wrapper providing character buffering and deterministic zeroing.
/// </summary>
public sealed class SecureInputWrapper : IDisposable
{
    private byte[]? _buffer;
    private int _length;

    public int Length => _length;

    public void AppendChars(ReadOnlySpan<char> chars)
    {
        byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(chars)];
        Encoding.UTF8.GetBytes(chars, utf8);
        int newLength = _length + utf8.Length;
        byte[] newBuffer = new byte[newLength];

        if (_buffer != null)
        {
            Array.Copy(_buffer, newBuffer, _length);
            CryptographicOperations.ZeroMemory(_buffer);
        }

        Array.Copy(utf8, 0, newBuffer, _length, utf8.Length);
        CryptographicOperations.ZeroMemory(utf8);

        _buffer = newBuffer;
        _length = newLength;
    }

    public string GetText()
    {
        if (_buffer == null || _length == 0) return string.Empty;
        return Encoding.UTF8.GetString(_buffer, 0, _length);
    }

    public byte[]? GetBytesAndClear()
    {
        var copy = _buffer;
        _buffer = null;
        _length = 0;
        return copy;
    }

    public void Clear()
    {
        if (_buffer != null)
        {
            CryptographicOperations.ZeroMemory(_buffer);
            _buffer = null;
        }
        _length = 0;
    }

    public void Dispose() => Clear();
}
