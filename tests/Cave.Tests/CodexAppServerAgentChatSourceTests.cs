using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Codex;

namespace Cave.Tests;

/// <summary>Verifies the exact-thread native history boundary with an isolated JSON-RPC process.</summary>
public sealed class CodexAppServerAgentChatSourceTests
{
    /// <summary>Only published and verifiable entries cross the boundary, even with misleading native candidates.</summary>
    [Fact]
    public async Task ProjectsPublishedEntriesAndVerifiesDiscoveredLineage()
    {
        var root = CreateFixtureDirectory();
        try
        {
            var command = await CreateServerAsync(root);
            var adapter = new CodexAppServerAgentChatSource(command);

            var result = await adapter.ReadAsync("root", ["foreign", "alien-child", "../unsafe"],
                DateTimeOffset.FromUnixTimeMilliseconds(2_000), CancellationToken.None);

            Assert.Equal(CodexAgentChatStatus.Available, result.Status);
            Assert.Collection(result.Threads,
                thread =>
                {
                    Assert.Equal("root", thread.ThreadId);
                    Assert.Null(thread.ParentThreadId);
                    Assert.Collection(thread.Entries,
                        prompt =>
                        {
                            Assert.Equal(ConversationMessageKind.Prompt, prompt.Kind);
                            Assert.Equal("The user request", prompt.Text);
                            Assert.Collection(prompt.Images,
                                inline => Assert.Equal("image/png", inline.ContentType),
                                unavailable => Assert.Null(unavailable.Bytes),
                                unavailable => Assert.Null(unavailable.Bytes),
                                unavailable => Assert.Null(unavailable.Bytes));
                        },
                        published => Assert.Equal("Published commentary", published.Text),
                        image => Assert.Equal("image/png", Assert.Single(image.Images).ContentType),
                        image => Assert.Null(Assert.Single(image.Images).Bytes),
                        final => Assert.Equal(ConversationMessageKind.Final, final.Kind));
                },
                thread =>
                {
                    Assert.Equal("child", thread.ThreadId);
                    Assert.Equal("root", thread.ParentThreadId);
                    var entry = Assert.Single(thread.Entries);
                    Assert.Equal("child-turn", entry.TurnId);
                    Assert.Equal("root-turn", entry.RootTurnId);
                    Assert.Equal("Child final reply", entry.Text);
                },
                thread =>
                {
                    Assert.Equal("grandchild", thread.ThreadId);
                    Assert.Equal("child", thread.ParentThreadId);
                    Assert.Equal("Grandchild reply", Assert.Single(thread.Entries).Text);
                });

            var calls = await File.ReadAllLinesAsync(Path.Combine(root, "calls.txt"));
            Assert.Contains("thread/read foreign", calls);
            Assert.Contains("thread/read alien-child", calls);
            Assert.DoesNotContain(calls, call => call.StartsWith("thread/items/list foreign", StringComparison.Ordinal)
                || call.StartsWith("thread/items/list alien-child", StringComparison.Ordinal)
                || call.StartsWith("thread/items/list child root-turn", StringComparison.Ordinal)
                || call.Contains("unsafe", StringComparison.Ordinal));
            Assert.Contains("thread/items/list root root-turn next-root", calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>An oversized response is discarded and the same exact item list is read with a smaller page.</summary>
    [Fact]
    public async Task OversizedPagesDoNotExposePayloadOrDropOtherThreads()
    {
        var root = CreateFixtureDirectory();
        try
        {
            var command = await CreateServerAsync(root, oversize: true);
            var adapter = new CodexAppServerAgentChatSource(command);

            var result = await adapter.ReadAsync("root", [],
                DateTimeOffset.FromUnixTimeMilliseconds(2_000), CancellationToken.None);

            Assert.Equal(CodexAgentChatStatus.Available, result.Status);
            Assert.Contains(result.Threads, thread => thread.ThreadId == "grandchild");
            Assert.Contains(result.Threads[0].Entries, entry => entry.Text == "Published commentary");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateFixtureDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cave-native-agent-chat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<string> CreateServerAsync(string directory, bool oversize = false)
    {
        var script = """
            import json, os, sys
            root = os.path.dirname(__file__)
            png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aB1sAAAAASUVORK5CYII='
            oversize = __OVERSIZE__
            ancestry = {'root': None, 'child': 'root', 'grandchild': 'child', 'foreign': None, 'alien-child': 'foreign'}
            def send(request, result):
                print(json.dumps({'id': request['id'], 'result': result}, separators=(',', ':')), flush=True)
            def entry(turn, id, type, at=2200, **data):
                return {'turnId': turn, 'startedAtMs': at or 2200, 'completedAtMs': at,
                    'item': {'id': id, 'type': type, **data}}
            def reply(turn, id, text, phase='commentary', at=2200, **other):
                return entry(turn, id, 'agentMessage', at=at, text=text, phase=phase, **other)
            ready = False
            for line in sys.stdin:
                request = json.loads(line)
                method = request.get('method')
                args = request.get('params', {})
                thread = args.get('threadId', '')
                turn = args.get('turnId', '')
                cursor = args.get('cursor', '')
                with open(os.path.join(root, 'calls.txt'), 'a', encoding='utf-8') as calls:
                    calls.write(' '.join(str(x) for x in (method, thread, turn, cursor) if x) + '\n')
                if method == 'initialize':
                    assert args['capabilities']['experimentalApi']
                    send(request, {'userAgent': 'CAVE synthetic fixture'})
                elif method == 'initialized':
                    ready = True
                elif method == 'thread/read':
                    assert ready and args['includeTurns'] is False
                    send(request, {'thread': {'id': thread, 'parentThreadId': ancestry[thread], 'createdAt': 2}})
                elif method == 'thread/turns/list':
                    assert ready and args['itemsView'] == 'notLoaded'
                    assert thread in ['root', 'child', 'grandchild']
                    own = {'id': thread + '-turn', 'rootTurnId': 'root-turn', 'startedAt': 2}
                    copied = {'id': 'root-turn', 'rootTurnId': 'root-turn', 'startedAt': 2}
                    send(request, {'data': [own] if thread == 'root' else [own, copied]})
                elif method == 'thread/items/list':
                    assert ready and args['deferOutput'] == {'commandExecution': True, 'mcpToolCall': True}
                    assert args['sortDirection'] == 'desc' and turn == thread + '-turn'
                    if thread == 'root':
                        if not cursor and oversize and args['limit'] > 4:
                            print('x' * (8 * 1024 * 1024 + 1), flush=True)
                            continue
                        if cursor:
                            assert cursor == 'next-root'
                            data = [
                                reply(turn, 'past', 'Before sharing', at=1999),
                                reply(turn, 'undated', 'Private timing unknown', at=None),
                                entry(turn, 'prompt', 'userMessage', at=2050, content=[
                                    {'type': 'text', 'text': '<in-app-browser-context source="ambient-ui-state">\nprivate marker\n</in-app-browser-context>\n## My request:\nThe user request'},
                                    {'type': 'image', 'url': 'data:image/png;base64,' + png},
                                    {'type': 'localImage', 'path': '/private/files.png'},
                                    {'type': 'image', 'url': 'data:image/jpeg;base64,' + png},
                                    {'type': 'image', 'url': 'https://private.example/png'},
                                ]),
                                entry(turn, 'child-id', 'subAgentActivity', at=2010, kind='started', agentThreadId='child'),
                            ]
                            send(request, {'data': data})
                        else:
                            data = [
                                reply(turn, 'final', 'Public final', phase='final_answer', at=2600),
                                entry(turn, 'no-resolver', 'imageGeneration', at=2500, status='completed', fileId='file-secret', savedPath='/private/image.png'),
                                entry(turn, 'native-image', 'imageGeneration', at=2400, status='completed', result=png),
                                reply(turn, 'published', 'Published commentary', at=2200),
                            ]
                            if args['limit'] > 4:
                                data.extend([
                                    entry(turn, 'private-reasoning', 'reasoning', text='Private model reasoning'),
                                    reply(turn, 'moderated', 'Hidden moderated output', moderation={'moderated': True}),
                                    reply(turn, 'native-policy', 'Restricted native output', moderation={'moderated': False, 'display': {'action': 'unknown'}}),
                                    reply(turn, 'ambiguous', 'Unknown phase', phase=None),
                                ])
                            send(request, {'data': data, 'nextCursor': 'next-root'})
                    elif thread == 'child':
                        send(request, {'data': [
                            reply(turn, 'child-final', 'Child final reply', phase='final_answer', at=2500),
                            entry(turn, 'grandchild-id', 'subAgentActivity', at=2400, kind='started', agentThreadId='grandchild'),
                        ]})
                    else:
                        assert thread == 'grandchild'
                        send(request, {'data': [reply(turn, 'grandchild-reply', 'Grandchild reply', at=2500)]})
                else:
                    raise AssertionError('Unexpected or non-read RPC ' + method)
            """.Replace("__OVERSIZE__", oversize ? "True" : "False", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory, "fake_agent_chat.py"), script);
        var command = Path.Combine(directory, OperatingSystem.IsWindows() ? "fake_codex.cmd" : "fake_codex");
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(command, "@echo off\r\npython \"%~dp0fake_agent_chat.py\" %*\r\n");
        }
        else
        {
            await File.WriteAllTextAsync(command, "#!/bin/sh\nexec python3 \"$(dirname \"$0\")/fake_agent_chat.py\" \"$@\"\n");
            File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return command;
    }
}
