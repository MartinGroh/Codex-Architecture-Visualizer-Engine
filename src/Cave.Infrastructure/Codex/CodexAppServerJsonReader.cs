using System.Text;
using System.Text.Json;

namespace Cave.Infrastructure.Codex;

/// <summary>Bounds JSON-line reads at the external process boundary before allocating complete payloads.</summary>
internal sealed class CodexAppServerJsonReader(StreamReader reader)
{
    private readonly char[] _buffer = new char[4096];
    private int _position;
    private int _count;

    internal async Task<JsonDocument> ReadResponseAsync(int id, CancellationToken cancellationToken)
    {
        for (var count = 0; count < 128; count++)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The Codex App Server closed before replying.");
            var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("method", out _)
                && document.RootElement.TryGetProperty("id", out var value)
                && value.TryGetInt32(out var number) && number == id)
                return document;
            document.Dispose();
        }
        throw new InvalidOperationException("The Codex App Server exceeded the bounded response count.");
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (_position == _count)
            {
                _count = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _position = 0;
                if (_count == 0) return line.Length == 0 ? null : line.ToString();
            }
            var character = _buffer[_position++];
            if (character == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= 131_072)
                throw new InvalidOperationException("The native response exceeded the 131072 character limit.");
            line.Append(character);
        }
    }
}
