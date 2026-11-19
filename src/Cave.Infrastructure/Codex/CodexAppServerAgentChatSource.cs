using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Codex;

/// <summary>Reads published task history through the existing read-only local Codex App Server.</summary>
public sealed class CodexAppServerAgentChatSource(string? codexCommand = null) : ICodexAgentChatSource
{
    private const int MaxThreads = 32;
    private const int MaxLineCharacters = 8 * 1024 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(12);

    /// <inheritdoc />
    public async Task<CodexAgentChatBatch> ReadAsync(string rootThreadId, IReadOnlyCollection<string> candidateThreadIds,
        DateTimeOffset sharedSinceUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateThreadIds);
        if (!AgentTelemetryIdentity.IsNative(rootThreadId))
            throw new ArgumentException("An exact native root thread identity is required.", nameof(rootThreadId));

        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        var batch = new Batch(rootThreadId, sharedSinceUtc);
        try
        {
            using var process = new Process { StartInfo = CodexAppServerProcess.CreateStartInfo(codexCommand) };
            if (!process.Start())
                return batch.Result();

            var stderr = DrainAsync(process.StandardError, timeout.Token);
            try
            {
                var reader = new CodexAppServerJsonReader(process.StandardOutput, MaxLineCharacters,
                    consumeOversizedLine: true);
                await batch.ReadAsync(process, reader, candidateThreadIds, timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                await CodexAppServerProcess.StopAsync(process).ConfigureAwait(false);
                await stderr.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            batch.Incomplete = true;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException
            or InvalidOperationException or TimeoutException or JsonException)
        {
            // Native diagnostics can contain URLs, paths, and history. Only return availability.
            batch.Incomplete = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return batch.Result();
    }

    private static async Task DrainAsync(StreamReader stream, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            while (await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0) { }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // Stderr is discarded and the process owner controls the read lifetime.
        }
    }

    private sealed class Batch(string rootThreadId, DateTimeOffset sharedSinceUtc)
    {
        private const int MaxThreadMetadata = 64;
        private const int MaxParentDepth = 16;
        private const int MaxRequests = 384;
        private const int MaxTurnPages = 2;
        private const int TurnPageSize = 16;
        private const int MaxItemPages = 24;
        private const int ItemPageSize = 16;
        private const int MaxMessages = 512;
        private const int MaxMessageCharacters = 24_000;
        private const int MaxImagesPerMessage = 4;
        private const int MaxImageBytes = 3 * 1024 * 1024;
        private const int MaxBatchImageBytes = 8 * 1024 * 1024;
        private static readonly Regex AmbientBrowserContext = new(
            """^<in-app-browser-context\s+source=["']ambient-ui-state["']>\s*\n.*?\n</in-app-browser-context>\s*""",
            RegexOptions.Singleline | RegexOptions.NonBacktracking);
        private readonly Dictionary<string, Metadata?> _metadata = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Thread> _verified = new(StringComparer.Ordinal);
        private readonly List<Thread> _order = [];
        private readonly PriorityQueue<Published, DateTimeOffset> _published = new();
        private int _requestId;
        private int _imageBytes;

        internal bool Incomplete { get; set; }

        internal async Task ReadAsync(Process process, CodexAppServerJsonReader reader,
            IReadOnlyCollection<string> candidates, CancellationToken token)
        {
            using var ready = await RequestAsync("initialize", new
            {
                clientInfo = new { name = "cave", version = "0.3.0" },
                capabilities = new { experimentalApi = true },
            }).ConfigureAwait(false);
            if (ready is null)
                return;

            await CodexAppServerProcess.WriteAsync(process, new { method = "initialized", @params = new { } }, token)
                .ConfigureAwait(false);
            var root = await ReadMetadataAsync(rootThreadId).ConfigureAwait(false);
            if (root is not { Parent: null })
                return;

            AddThread(root);
            // Read the root first so an unavailable or unrelated candidate never starves it.
            await ReadThreadAsync(_order[0]).ConfigureAwait(false);
            foreach (var candidate in candidates.Take(MaxThreadMetadata))
                await VerifyLineageAsync(candidate, null).ConfigureAwait(false);
            if (candidates.Count > MaxThreadMetadata)
                Incomplete = true;

            for (var index = 1; index < _order.Count; index++)
                await ReadThreadAsync(_order[index]).ConfigureAwait(false);

            async Task<Metadata?> ReadMetadataAsync(string threadId)
            {
                if (_metadata.TryGetValue(threadId, out var cached))
                    return cached;
                if (_metadata.Count >= MaxThreadMetadata)
                {
                    Incomplete = true;
                    return null;
                }

                using var response = await RequestAsync("thread/read", new { threadId, includeTurns = false })
                    .ConfigureAwait(false);
                var value = Property(ResultOf(response), "thread");
                var parent = NativeId(value, "parentThreadId");
                // A malformed non-null parent is not a root marker.
                var validParent = parent is not null
                    || Property(value, "parentThreadId").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
                var metadata = NativeId(value, "id") == threadId && validParent
                    ? new Metadata(threadId, parent, Integer(value, "createdAt"))
                    : null;
                _metadata.Add(threadId, metadata);
                return metadata;
            }

            async Task VerifyLineageAsync(string candidate, string? expectedParent)
            {
                if (!AgentTelemetryIdentity.IsNative(candidate) || _verified.ContainsKey(candidate))
                    return;

                var path = new List<Metadata>();
                var id = candidate;
                while (!_verified.ContainsKey(id) && path.Count < MaxParentDepth)
                {
                    var metadata = await ReadMetadataAsync(id).ConfigureAwait(false);
                    if (metadata?.Parent is null || path.Any(part => part.Id == id)
                        || path.Count == 0 && expectedParent is not null && metadata.Parent != expectedParent)
                        return;
                    path.Add(metadata);
                    id = metadata.Parent;
                }

                if (!_verified.ContainsKey(id))
                    return;
                if (_order.Count + path.Count > MaxThreads)
                {
                    Incomplete = true;
                    return;
                }

                for (var index = path.Count - 1; index >= 0; index--)
                    AddThread(path[index]);
            }

            async Task ReadThreadAsync(Thread thread)
            {
                var discoveries = new List<(string Id, DateTimeOffset At)>();
                string? cursor = null;
                var itemPages = 0;
                for (var page = 0; page < MaxTurnPages; page++)
                {
                    using var response = await RequestAsync("thread/turns/list", new
                    {
                        threadId = thread.Metadata.Id,
                        cursor,
                        limit = TurnPageSize,
                        itemsView = "notLoaded",
                        sortDirection = "desc",
                    }).ConfigureAwait(false);
                    var result = ResultOf(response);
                    if (!Page(result, out var turns, TurnPageSize))
                    {
                        thread.MarkIncomplete();
                        break;
                    }

                    // Capture ancestor turn identities independently of their message contents.
                    foreach (var turn in turns.EnumerateArray())
                        if (NativeId(turn, "id") is { } turnId)
                            thread.Turns.Add(turnId);

                    foreach (var turn in turns.EnumerateArray())
                    {
                        if (Integer(turn, "completedAt") < sharedSinceUtc.ToUnixTimeSeconds())
                            continue;

                        var turnId = NativeId(turn, "id");
                        var rootTurnId = NativeId(turn, "rootTurnId");
                        if (turnId is null || !OwnsTurn(thread, turn, turnId, rootTurnId))
                            continue;

                        string? itemCursor = null;
                        var itemPageSize = ItemPageSize;
                        while (true)
                        {
                            if (++itemPages > MaxItemPages)
                            {
                                thread.MarkIncomplete();
                                break;
                            }

                            JsonDocument? document;
                            try
                            {
                                document = await RequestAsync("thread/items/list", new
                                {
                                    threadId = thread.Metadata.Id,
                                    turnId,
                                    cursor = itemCursor,
                                    limit = itemPageSize,
                                    sortDirection = "desc",
                                    deferOutput = new { commandExecution = true, mcpToolCall = true },
                                }).ConfigureAwait(false);
                            }
                            catch (InvalidDataException) when (itemPageSize > 1)
                            {
                                // Re-page the same bounded source to isolate oversized tool or image output.
                                itemPageSize = Math.Max(1, itemPageSize / 4);
                                continue;
                            }
                            catch (InvalidDataException)
                            {
                                thread.MarkIncomplete();
                                break;
                            }

                            using (document)
                            {
                                var itemsResult = ResultOf(document);
                                if (!Page(itemsResult, out var items, itemPageSize))
                                {
                                    thread.MarkIncomplete();
                                    break;
                                }

                                thread.ReadSucceeded = true;
                                foreach (var entry in items.EnumerateArray())
                                    Collect(thread, turnId, rootTurnId, entry, discoveries);
                                var next = Cursor(itemsResult);
                                if (next is null)
                                    break;
                                if (next == itemCursor)
                                {
                                    thread.MarkIncomplete();
                                    break;
                                }
                                itemCursor = next;
                            }
                        }
                        if (itemPages >= MaxItemPages)
                            break;
                    }

                    if (itemPages >= MaxItemPages)
                    {
                        thread.MarkIncomplete();
                        break;
                    }

                    if (itemPages == 0)
                        thread.ReadSucceeded = true;
                    var nextTurnPage = Cursor(result);
                    if (nextTurnPage is null)
                        break;
                    if (nextTurnPage == cursor || page == MaxTurnPages - 1)
                    {
                        thread.MarkIncomplete();
                        break;
                    }
                    cursor = nextTurnPage;
                }
                foreach (var (id, _) in discoveries.OrderBy(item => item.At))
                    await VerifyLineageAsync(id, thread.Metadata.Id).ConfigureAwait(false);
            }

            async Task<JsonDocument?> RequestAsync(string method, object parameters)
            {
                var id = ++_requestId;
                if (id > MaxRequests)
                    throw new InvalidOperationException("The bounded chat request count was reached.");
                await CodexAppServerProcess.WriteAsync(process, new { method, id, @params = parameters }, token)
                    .ConfigureAwait(false);
                var response = await reader.ReadResponseAsync(id, token).ConfigureAwait(false);
                if (response.RootElement.TryGetProperty("result", out _))
                    return response;
                response.Dispose();
                return null;
            }
        }

        private void AddThread(Metadata metadata)
        {
            var thread = new Thread(metadata);
            _verified.Add(metadata.Id, thread);
            _order.Add(thread);
        }

        private bool OwnsTurn(Thread thread, JsonElement turn, string turnId, string? rootTurnId)
        {
            if (thread.Metadata.Parent is null)
                return true;

            // Forked child context can contain the same prior turns as its ancestors. Publish
            // only delegated turns with native timing inside the child's actual lifetime.
            if (rootTurnId is null || rootTurnId == turnId || thread.Metadata.CreatedAt is not { } createdAt
                || Integer(turn, "startedAt") is not { } startedAt || startedAt < createdAt)
                return false;
            for (var parent = thread.Metadata.Parent; parent is not null; parent = _verified[parent].Metadata.Parent)
                if (_verified[parent].Turns.Contains(turnId))
                    return false;
            return true;
        }

        private void Collect(Thread thread, string turnId, string? rootTurnId, JsonElement entry,
            List<(string Id, DateTimeOffset At)> discoveries)
        {
            if (NativeId(entry, "turnId") != turnId)
                return;
            var item = Property(entry, "item");
            if (NativeId(item, "id") is not { } itemId || !thread.Items.Add((turnId, itemId)))
                return;

            var type = Text(item, "type");
            if (type == "subAgentActivity" && Text(item, "kind") == "started"
                && NativeId(item, "agentThreadId") is { } child
                && Timestamp(entry, "startedAtMs") is { } discoveredAt)
                discoveries.Add((child, discoveredAt));
            if (type == "collabAgentToolCall" && Text(item, "tool") == "spawnAgent"
                && NativeId(item, "senderThreadId") == thread.Metadata.Id
                && Timestamp(entry, "startedAtMs") is { } spawnedAt)
            {
                var targets = Property(item, "receiverThreadIds");
                if (targets.ValueKind == JsonValueKind.Array && targets.GetArrayLength() <= MaxThreads)
                    foreach (var target in targets.EnumerateArray())
                        if (target.ValueKind == JsonValueKind.String && AgentTelemetryIdentity.IsNative(target.GetString()))
                            discoveries.Add((target.GetString()!, spawnedAt));
            }

            if (Timestamp(entry, "completedAtMs") is not { } publishedAt || publishedAt < sharedSinceUtc)
                return;

            var message = ReadPublishedItem(type, item);
            if (message is not { } content)
                return;
            var projected = new CodexAgentChatEntry(itemId, turnId, rootTurnId, content.Role, content.Kind,
                publishedAt, content.Text, content.IsTruncated, content.Images);
            _published.Enqueue(new Published(thread.Metadata.Id, projected), publishedAt);
            if (_published.Count > MaxMessages)
            {
                _published.Dequeue();
                Incomplete = true;
            }
        }

        private Message? ReadPublishedItem(string? type, JsonElement item)
        {
            switch (type)
            {
                case "agentMessage":
                    var moderation = Property(item, "moderation");
                    if (moderation.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                        && (Property(moderation, "moderated").ValueKind != JsonValueKind.False
                            || Property(moderation, "display").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)))
                        return null;
                    var kind = Text(item, "phase") switch
                    {
                        "commentary" or "partial_answer" => ConversationMessageKind.Commentary,
                        "final_answer" => ConversationMessageKind.Final,
                        _ => (ConversationMessageKind?)null,
                    };
                    var text = Text(item, "text");
                    if (kind is null || string.IsNullOrWhiteSpace(text))
                        return null;
                    var bounded = Bound(text);
                    return new Message(ConversationRole.Assistant, kind.Value, bounded, bounded.Length < text.Length, []);
                case "userMessage":
                    return ReadPrompt(item);
                case "imageGeneration" when Text(item, "status") == "completed":
                    var image = string.IsNullOrEmpty(Text(item, "fileId")) && Property(item, "failure").ValueKind
                            is JsonValueKind.Undefined or JsonValueKind.Null
                        ? ReadImage(Text(item, "result"), allowUnlabelled: true)
                        : new CodexAgentChatImage(null, null);
                    return new Message(ConversationRole.Assistant, ConversationMessageKind.Commentary, "", false, [image]);
                default:
                    return null;
            }
        }

        private Message? ReadPrompt(JsonElement item)
        {
            var parts = Property(item, "content");
            if (parts.ValueKind != JsonValueKind.Array)
                return null;
            var chunks = new List<string>();
            var images = new List<CodexAgentChatImage>();
            var used = 0;
            var clipped = false;
            foreach (var part in parts.EnumerateArray())
            {
                var type = Text(part, "type");
                if (type == "text" && Text(part, "text") is { } text)
                {
                    // Do not re-publish the host's internal browser continuation envelope.
                    if (text.StartsWith("CAVE_BROWSER_MESSAGES_V1\n", StringComparison.Ordinal))
                        return null;
                    var envelope = AmbientBrowserContext.Match(text);
                    if (envelope.Success)
                    {
                        text = text[envelope.Length..];
                        const string heading = "## My request:";
                        if (text.StartsWith(heading, StringComparison.OrdinalIgnoreCase))
                            text = text[heading.Length..].TrimStart();
                    }
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    var remaining = Math.Max(0, MaxMessageCharacters - used - (chunks.Count > 0 ? 1 : 0));
                    var bounded = Bound(text, remaining);
                    clipped |= bounded.Length < text.Length;
                    if (bounded.Length > 0)
                    {
                        used += bounded.Length + (chunks.Count > 0 ? 1 : 0);
                        chunks.Add(bounded);
                    }
                }
                else if (type is "image" or "localImage" && images.Count < MaxImagesPerMessage)
                {
                    images.Add(type == "image" && string.IsNullOrEmpty(Text(part, "fileId"))
                        ? ReadImage(Text(part, "url"), allowUnlabelled: false)
                        : new CodexAgentChatImage(null, null));
                }
            }
            return chunks.Count == 0 && images.Count == 0
                ? null
                : new Message(ConversationRole.User, ConversationMessageKind.Prompt, string.Join('\n', chunks), clipped, images);
        }

        private CodexAgentChatImage ReadImage(string? encoded, bool allowUnlabelled)
        {
            var unavailable = new CodexAgentChatImage(null, null);
            if (string.IsNullOrEmpty(encoded))
                return unavailable;
            var payload = encoded.AsSpan();
            string? declaredMediaType = null;
            if (payload.StartsWith("data:"))
            {
                var semicolon = payload.IndexOf(';');
                if (semicolon is < 6 or > 20 || !payload[semicolon..].StartsWith(";base64,"))
                    return unavailable;
                declaredMediaType = payload[5..semicolon].ToString();
                if (declaredMediaType is not ("image/png" or "image/jpeg" or "image/webp"))
                    return unavailable;
                payload = payload[(semicolon + 8)..];
            }
            else if (!allowUnlabelled)
                return unavailable;

            var available = Math.Min(MaxImageBytes, MaxBatchImageBytes - _imageBytes);
            if (payload.Length == 0 || payload.Length > (available + 2) / 3 * 4)
                return unavailable;
            var bytes = new byte[payload.Length / 4 * 3];
            if (!Convert.TryFromBase64Chars(payload, bytes, out var written) || written == 0 || written > available)
                return unavailable;
            var detectedMediaType = RasterMediaType(bytes.AsSpan(0, written));
            if (detectedMediaType is null || declaredMediaType is not null && detectedMediaType != declaredMediaType)
                return unavailable;
            _imageBytes += written;
            Array.Resize(ref bytes, written);
            return new CodexAgentChatImage(detectedMediaType, bytes);
        }

        internal CodexAgentChatBatch Result()
        {
            var entries = _published.UnorderedItems.ToLookup(item => item.Element.ThreadId, item => item.Element.Entry);
            var threads = _order.Select(thread => new CodexAgentChatThread(thread.Metadata.Id, thread.Metadata.Parent,
                thread.ReadSucceeded
                    ? thread.Incomplete ? CodexAgentChatStatus.Partial : CodexAgentChatStatus.Available
                    : CodexAgentChatStatus.Unavailable,
                entries[thread.Metadata.Id].OrderBy(entry => entry.PublishedAtUtc)
                    .ThenBy(entry => entry.ItemId, StringComparer.Ordinal).ToArray())).ToArray();
            var status = threads.All(thread => thread.Status == CodexAgentChatStatus.Unavailable)
                ? CodexAgentChatStatus.Unavailable
                : Incomplete || threads.Any(thread => thread.Status != CodexAgentChatStatus.Available)
                    ? CodexAgentChatStatus.Partial
                    : CodexAgentChatStatus.Available;
            return new CodexAgentChatBatch(rootThreadId, status, threads);
        }

        private static string Bound(string value, int maximum = MaxMessageCharacters)
        {
            if (value.Length <= maximum)
                return value;
            if (maximum > 0 && char.IsHighSurrogate(value[maximum - 1]))
                maximum--;
            return value[..maximum];
        }

        private sealed record Metadata(string Id, string? Parent, long? CreatedAt);
        private sealed class Thread(Metadata metadata)
        {
            internal Metadata Metadata { get; } = metadata;
            internal HashSet<string> Turns { get; } = new(StringComparer.Ordinal);
            internal HashSet<(string Turn, string Item)> Items { get; } = [];
            internal bool ReadSucceeded { get; set; }
            internal bool Incomplete { get; private set; }
            internal void MarkIncomplete() => Incomplete = true;
        }
        private sealed record Message(ConversationRole Role, ConversationMessageKind Kind, string Text,
            bool IsTruncated, IReadOnlyList<CodexAgentChatImage> Images);
        private sealed record Published(string ThreadId, CodexAgentChatEntry Entry);
    }

    private static string? RasterMediaType(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length >= 33 && data.StartsWith(pngSignature)
            && BinaryPrimitives.ReadUInt32BigEndian(data[8..]) == 13 && data[12..16].SequenceEqual("IHDR"u8)
            && ValidResolution(BinaryPrimitives.ReadUInt32BigEndian(data[16..]), BinaryPrimitives.ReadUInt32BigEndian(data[20..])))
            return "image/png";
        if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8 && ValidJpegResolution(data))
            return "image/jpeg";
        if (data.Length >= 25 && data[..4].SequenceEqual("RIFF"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == data.Length - 8
            && data[8..12].SequenceEqual("WEBP"u8) && ValidWebPResolution(data))
            return "image/webp";
        return null;
    }

    private static bool ValidJpegResolution(ReadOnlySpan<byte> data)
    {
        var offset = 2;
        while (offset + 3 < data.Length && data[offset++] == 0xff)
        {
            while (offset < data.Length && data[offset] == 0xff)
                offset++;
            if (offset + 2 >= data.Length)
                break;
            var marker = data[offset++];
            if (marker is 0xd9 or 0xda)
                break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            if (length < 2 || length > data.Length - offset)
                break;
            if (marker is >= 0xc0 and <= 0xcf and not (0xc4 or 0xc8 or 0xcc))
                return length >= 8 && ValidResolution(BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 3)..]));
            offset += length;
        }
        return false;
    }

    private static bool ValidWebPResolution(ReadOnlySpan<byte> data)
    {
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        var chunk = data[12..16];
        if (size > data.Length - 20)
            return false;
        if (chunk.SequenceEqual("VP8X"u8) && size >= 10)
            return ValidResolution(1 + TwentyFourBits(data[24..]), 1 + TwentyFourBits(data[27..]));
        if (chunk.SequenceEqual("VP8L"u8) && size >= 5 && data[20] == 0x2f)
        {
            var dimensions = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            return ValidResolution(1 + (dimensions & 0x3fff), 1 + ((dimensions >> 14) & 0x3fff));
        }
        if (chunk.SequenceEqual("VP8 "u8) && size >= 10 && data[23..26].SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
            return ValidResolution(0x3fffU & BinaryPrimitives.ReadUInt16LittleEndian(data[26..]),
                0x3fffU & BinaryPrimitives.ReadUInt16LittleEndian(data[28..]));
        return false;
    }

    private static uint TwentyFourBits(ReadOnlySpan<byte> bytes) => (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16);
    private static bool ValidResolution(uint width, uint height) =>
        width is > 0 and <= 8192 && height is > 0 and <= 8192 && (ulong)width * height <= 32_000_000;

    private static JsonElement ResultOf(JsonDocument? document) =>
        document is null ? default : Property(document.RootElement, "result");
    private static bool Page(JsonElement result, out JsonElement entries, int limit)
    {
        entries = Property(result, "data");
        return entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() <= limit;
    }
    private static string? Cursor(JsonElement result) =>
        Text(result, "nextCursor") is { Length: > 0 and <= 2048 } cursor ? cursor : null;
    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
    private static string? Text(JsonElement value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } property ? property.GetString() : null;
    private static string? NativeId(JsonElement value, string name) =>
        Text(value, name) is { Length: <= 128 } id && AgentTelemetryIdentity.IsNative(id) ? id : null;
    private static long? Integer(JsonElement value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.Number } property && property.TryGetInt64(out var number)
            ? number : null;
    private static DateTimeOffset? Timestamp(JsonElement value, string name) =>
        Integer(value, name) is > 0 and <= 253402300799999L and var milliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null;
}
