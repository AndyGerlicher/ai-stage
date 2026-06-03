using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentSessions.Providers.GitHubCopilot.Internal;

/// <summary>
/// Tails an append-only events.jsonl file. Reads at the byte level and splits
/// on '\n' so an unterminated trailing line is held over until the next poll
/// rather than being lost or causing a UTF-8 mid-codepoint resume.
///
/// Counts in-flight tool/assistant/hook operations so a host can decide
/// whether the session is Processing or Idle.
/// </summary>
internal sealed class EventsJsonlTailer
{
    private readonly string _path;

    /// <summary>Absolute byte offset of the next byte to read from the file.</summary>
    private long _readCursor;

    /// <summary>Bytes read past the last '\n' that did not yet form a complete line.</summary>
    private byte[] _carry = Array.Empty<byte>();

    private int _openTools;
    private int _openAssistantMessages;
    private int _openAssistantTurns;
    private int _openHooks;

    public EventsJsonlTailer(string path)
    {
        _path = path;
    }

    /// <summary>Whether any open operation was observed at the last poll.</summary>
    public bool IsProcessing => _openTools > 0 || _openAssistantMessages > 0 || _openAssistantTurns > 0 || _openHooks > 0;

    /// <summary>
    /// Reads any new bytes appended since the previous poll and updates the
    /// open-operation counters. Returns true if anything was read.
    /// </summary>
    public bool Poll()
    {
        if (!File.Exists(_path))
        {
            ResetState();
            return false;
        }

        long length;
        try { length = new FileInfo(_path).Length; }
        catch { return false; }

        if (length < _readCursor)
        {
            // File rotated/truncated.
            ResetState();
        }

        if (length == _readCursor)
            return false;

        byte[] buffer;
        int bytesRead;
        try
        {
            using var fs = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_readCursor, SeekOrigin.Begin);
            int toRead = checked((int)Math.Min(length - _readCursor, int.MaxValue));
            buffer = new byte[toRead];
            bytesRead = 0;
            while (bytesRead < toRead)
            {
                int n = fs.Read(buffer, bytesRead, toRead - bytesRead);
                if (n <= 0) break;
                bytesRead += n;
            }
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        if (bytesRead == 0)
            return false;

        _readCursor += bytesRead;
        ConsumeBytes(buffer, bytesRead);
        return true;
    }

    private void ResetState()
    {
        _readCursor = 0;
        _carry = Array.Empty<byte>();
        _openTools = 0;
        _openAssistantMessages = 0;
        _openAssistantTurns = 0;
        _openHooks = 0;
    }

    /// <summary>
    /// Walks <paramref name="buffer"/> splitting on '\n'. Each complete line
    /// (carry + slice up to newline) is decoded and dispatched. Trailing bytes
    /// that did not end with '\n' become the new carry.
    /// </summary>
    private void ConsumeBytes(byte[] buffer, int count)
    {
        int sliceStart = 0;
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] != (byte)'\n') continue;

            int sliceLen = i - sliceStart;
            DispatchLine(buffer, sliceStart, sliceLen);
            sliceStart = i + 1;
        }

        // Whatever is left after the last newline becomes the carry.
        int carryLen = count - sliceStart;
        if (carryLen == 0)
        {
            _carry = Array.Empty<byte>();
        }
        else
        {
            var next = new byte[carryLen];
            Array.Copy(buffer, sliceStart, next, 0, carryLen);
            _carry = next;
        }
    }

    private void DispatchLine(byte[] buffer, int sliceStart, int sliceLen)
    {
        string line;
        if (_carry.Length == 0)
        {
            line = Encoding.UTF8.GetString(buffer, sliceStart, sliceLen);
        }
        else
        {
            int total = _carry.Length + sliceLen;
            var combined = new byte[total];
            Array.Copy(_carry, 0, combined, 0, _carry.Length);
            if (sliceLen > 0)
                Array.Copy(buffer, sliceStart, combined, _carry.Length, sliceLen);
            line = Encoding.UTF8.GetString(combined);
            _carry = Array.Empty<byte>();
        }

        // Strip CR from CRLF writers.
        if (line.Length > 0 && line[^1] == '\r')
            line = line[..^1];

        if (line.Length == 0) return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                return;
            string? type = typeProp.GetString();
            switch (type)
            {
                case "tool.execution_start":
                    _openTools++;
                    break;
                case "tool.execution_complete":
                    if (_openTools > 0) _openTools--;
                    break;
                case "assistant.message_start":
                    _openAssistantMessages++;
                    break;
                case "assistant.message_complete":
                    if (_openAssistantMessages > 0) _openAssistantMessages--;
                    break;
                case "assistant.turn_start":
                    _openAssistantTurns++;
                    break;
                case "assistant.turn_end":
                    if (_openAssistantTurns > 0) _openAssistantTurns--;
                    break;
                case "hook.start":
                    _openHooks++;
                    break;
                case "hook.end":
                    if (_openHooks > 0) _openHooks--;
                    break;
            }
        }
        catch (JsonException)
        {
            // Malformed line — drop it and continue.
        }
    }
}
