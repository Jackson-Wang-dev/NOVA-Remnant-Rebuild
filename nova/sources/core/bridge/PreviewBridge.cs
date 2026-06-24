#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Godot;
using Nova.Exceptions;
using Nova.Parser;

namespace Nova;

/// <summary>
/// DEBUG-only TCP bridge for VVN (e:\nova2\vvn) to trigger hot reload / seek operations.
/// Listens on localhost TCP, processes commands on the main thread via queue drain in Tick().
/// Protocol: newline-separated JSON, integer id for request/response pairing.
/// </summary>
public class PreviewBridge : ISingleton
{
    private const int DefaultPort = 9999;

    private System.Net.Sockets.TcpListener _listener;
    private Thread _acceptThread;
    private readonly ConcurrentQueue<(string Message, NetworkStream Stream)> _messageQueue = new();
    private readonly ConcurrentDictionary<TcpClient, NetworkStream> _clients = new();
    private bool _disposed;

    private int _port;
    private bool _serverStarted;

    public int Port => _port;

    public static PreviewBridge Instance => NovaController.Instance.GetObj<PreviewBridge>();

    public void OnEnter()
    {
        _port = GetPortFromEnvironment();
        StartServer();
    }

    public void OnReady()
    {
        // GameState is guaranteed initialized by now (OnReady runs for every singleton only after
        // every OnEnter has completed), so it's safe to subscribe here rather than in OnEnter().
        GameState.Instance.DialogueChanged.Subscribe(_ => PushStateChanged());
    }

    public void OnExit()
    {
        StopServer();
    }

    private static int GetPortFromEnvironment()
    {
        // Allow override via environment variable or command line argument
        var cmdlineArgs = OS.GetCmdlineArgs();
        for (var i = 0; i < cmdlineArgs.Length - 1; i++)
        {
            if (cmdlineArgs[i] == "--preview-bridge-port" || cmdlineArgs[i] == "--pbridge-port")
            {
                if (int.TryParse(cmdlineArgs[i + 1], out var port) && port > 0 && port <= 65535)
                {
                    return port;
                }
            }
        }

        var envPort = System.Environment.GetEnvironmentVariable("NOVA_PREVIEW_BRIDGE_PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var envPortVal) && envPortVal > 0 && envPortVal <= 65535)
        {
            return envPortVal;
        }

        return DefaultPort;
    }

    private void StartServer()
    {
        if (_serverStarted) return;
        _serverStarted = true;

        // Another debug instance (e.g. a manual playtest session running alongside a headless test
        // run) may already hold this port - that must not take down the whole singleton init via
        // NovaController's catch-all (which would Utils.Quit() the entire engine for an unrelated
        // dev-tooling port clash). Log and leave the bridge inert instead.
        try
        {
            _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, _port);
            _listener.Start();
        }
        catch (SocketException e)
        {
            GD.PrintErr($"[PreviewBridge] Failed to bind localhost:{_port}, bridge disabled: {e.Message}");
            _listener = null;
            return;
        }
        GD.Print($"[PreviewBridge] Listening on localhost:{_port}");

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "PreviewBridge.Accept" };
        _acceptThread.Start();
    }

    private void StopServer()
    {
        _disposed = true;
        _listener?.Stop();
        _acceptThread?.Join(1000);
        _listener = null;
    }

    private void AcceptLoop()
    {
        while (!_disposed)
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                var thread = new Thread(() => HandleClient(client)) { IsBackground = true, Name = "PreviewBridge.Client" };
                thread.Start();
            }
            catch (SocketException) when (_disposed)
            {
                break;
            }
            catch (Exception e)
            {
                GD.PrintErr($"[PreviewBridge] Accept error: {e}");
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        client.NoDelay = true;
        var stream = client.GetStream();
        _clients[client] = stream;
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        try
        {
            while (!_disposed && client.Connected)
            {
                try
                {
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    for (var i = 0; i < bytesRead; i++)
                    {
                        var c = (char)buffer[i];
                        if (c == '\n')
                        {
                            var line = sb.ToString();
                            sb.Clear();
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                _messageQueue.Enqueue((line, stream));
                            }
                        }
                        else if (c != '\r')
                        {
                            sb.Append(c);
                        }
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[PreviewBridge] Read error: {e}");
                    break;
                }
            }
        }
        finally
        {
            _clients.TryRemove(client, out _);
            client.Close();
        }
    }

    /// <summary>
    /// Called from NovaController._Process() on the main thread to drain the message queue.
    /// </summary>
    public void Tick()
    {
        while (_messageQueue.TryDequeue(out var entry))
        {
            ProcessMessage(entry.Message, entry.Stream);
        }
    }

    private void ProcessMessage(string message, NetworkStream replyStream)
    {
        // Handle state_changed push (no id)
        if (message.TrimStart().StartsWith("{\"method\":\"state_changed\"", StringComparison.OrdinalIgnoreCase))
        {
            // Passive push from VVN side acknowledging they received state - ignore
            return;
        }

        int? requestId = null;
        string method = null;
        var parameters = new Dictionary<string, object>();

        try
        {
            // Simple JSON parsing without external dependencies
            // Expected format: {"id":123,"method":"reload"} or {"id":123,"method":"seek","params":{"nodeRecordId":1,"dialogueIndex":0}}
            var json = ParseJson(message);
            if (json.TryGetValue("id", out var idVal) && idVal is System.Numerics.BigInteger id)
            {
                requestId = (int)id;
            }
            if (json.TryGetValue("method", out var methodVal))
            {
                method = methodVal as string;
            }
            if (json.TryGetValue("params", out var paramsVal) && paramsVal is Dictionary<string, object> ps)
            {
                parameters = ps;
            }
        }
        catch
        {
            if (requestId.HasValue)
            {
                SendResponse(replyStream, requestId.Value, new { ok = false, error = new { message = "Invalid JSON format" } });
            }
            return;
        }

        if (string.IsNullOrEmpty(method))
        {
            if (requestId.HasValue)
            {
                SendResponse(replyStream, requestId.Value, new { ok = false, error = new { message = "Missing 'method' field" } });
            }
            return;
        }

        object result = null;
        switch (method)
        {
            case "reload":
                result = HandleReload();
                break;
            case "seek":
                {
                    var nodeRecordId = GetIntParam(parameters, "nodeRecordId");
                    var dialogueIndex = GetIntParam(parameters, "dialogueIndex");
                    if (nodeRecordId.HasValue && dialogueIndex.HasValue)
                    {
                        result = HandleSeek(nodeRecordId.Value, dialogueIndex.Value);
                    }
                    else
                    {
                        result = new { ok = false, error = new { message = "Missing nodeRecordId or dialogueIndex" } };
                    }
                    break;
                }
            case "get_state":
                result = HandleGetState();
                break;
            default:
                result = new { ok = false, error = new { message = $"Unknown method: {method}" } };
                break;
        }

        if (requestId.HasValue && result != null)
        {
            SendResponse(replyStream, requestId.Value, result);
        }
    }

    private static int? GetIntParam(Dictionary<string, object> parameters, string key)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            if (val is System.Numerics.BigInteger bi) return (int)bi;
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is double d) return (int)d;
            if (val is string s && int.TryParse(s, out var parsed)) return parsed;
        }
        return null;
    }

    private object HandleReload()
    {
        try
        {
            GameState.Instance.ReloadScripts();
            return new { ok = true };
        }
        catch (ParserException e)
        {
            return new { ok = false, error = new { message = e.Message, line = e.Line, column = e.Column } };
        }
        catch (ScriptLoadingException e)
        {
            return new { ok = false, error = new { message = e.Message } };
        }
        catch (Exception e)
        {
            return new { ok = false, error = new { message = e.Message } };
        }
    }

    private object HandleSeek(int nodeRecordId, int dialogueIndex)
    {
        try
        {
            GameState.Instance.MoveTo(nodeRecordId, dialogueIndex);
            return new { ok = true };
        }
        catch (Exception e)
        {
            return new { ok = false, error = new { message = e.Message } };
        }
    }

    private object HandleGetState()
    {
        var gameState = GameState.Instance;
        var startNodeNames = new List<string>();
        foreach (var name in gameState.GetStartNodeNames(StartNodeType.All))
        {
            startNodeNames.Add(name);
        }

        return new
        {
            ok = true,
            state = new
            {
                currentNodeRecordId = gameState.CurrentNodeRecordId,
                currentDialogueIndex = gameState.CurrentDialogueIndex,
                startNodeNames
            }
        };
    }

    private void SendResponse(NetworkStream replyStream, int requestId, object response)
    {
        var payload = new Dictionary<string, object> { ["id"] = requestId };
        foreach (var prop in response.GetType().GetProperties())
        {
            payload[prop.Name] = prop.GetValue(response);
        }
        WriteLine(replyStream, SimpleJsonSerialize(payload));
    }

    /// <summary>
    /// Push state changed event to every connected VVN client.
    /// Called when GameState position changes (via debug keys R/N/P or manual navigation).
    /// </summary>
    public void PushStateChanged()
    {
        var state = HandleGetState();
        var payload = new Dictionary<string, object> { ["method"] = "state_changed" };
        foreach (var prop in state.GetType().GetProperties())
        {
            payload[prop.Name] = prop.GetValue(state);
        }
        var line = SimpleJsonSerialize(payload);
        foreach (var stream in _clients.Values)
        {
            WriteLine(stream, line);
        }
    }

    private static void WriteLine(NetworkStream stream, string json)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[PreviewBridge] Write error: {e}");
        }
    }

    private static string SimpleJsonSerialize(object obj)
    {
        if (obj == null) return "null";

        var type = obj.GetType();

        if (type == typeof(bool))
            return (bool)obj ? "true" : "false";

        if (type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float))
            return obj.ToString();

        if (obj is string s)
            return $"\"{EscapeJsonString(s)}\"";

        if (obj is System.Numerics.BigInteger bi)
            return bi.ToString();

        if (obj is System.Collections.IDictionary dict)
        {
            var entries = new List<string>();
            foreach (var key in dict.Keys)
            {
                entries.Add($"\"{EscapeJsonString(key.ToString())}\":{SimpleJsonSerialize(dict[key])}");
            }
            return "{" + string.Join(",", entries) + "}";
        }

        if (obj is System.Collections.IEnumerable enumerable)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                items.Add(SimpleJsonSerialize(item));
            }
            return "[" + string.Join(",", items) + "]";
        }

        // Anonymous objects from anonymous types
        var properties = type.GetProperties();
        if (properties.Length > 0)
        {
            var parts = new List<string>();
            foreach (var prop in properties)
            {
                var val = prop.GetValue(obj);
                parts.Add($"\"{prop.Name}\":{SimpleJsonSerialize(val)}");
            }
            return "{" + string.Join(",", parts) + "}";
        }

        return "\"unknown\"";
    }

    private static string EscapeJsonString(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, object> ParseJson(string json)
    {
        var result = new Dictionary<string, object>();
        json = json.Trim();

        if (json.StartsWith("{") && json.EndsWith("}"))
        {
            json = json.Substring(1, json.Length - 2);
            ParseObject(json, result);
        }

        return result;
    }

    private static void ParseObject(string content, Dictionary<string, object> result)
    {
        var i = 0;
        while (i < content.Length)
        {
            // Skip whitespace and comma
            while (i < content.Length && (char.IsWhiteSpace(content[i]) || content[i] == ','))
                i++;

            if (i >= content.Length) break;

            // Parse key
            if (content[i] != '"')
            {
                i++;
                continue;
            }

            i++; // skip opening quote
            var keyEnd = content.IndexOf('"', i);
            var key = content.Substring(i, keyEnd - i);
            i = keyEnd + 1;

            // Skip to colon
            while (i < content.Length && content[i] != ':')
                i++;
            i++; // skip colon

            // Skip whitespace
            while (i < content.Length && char.IsWhiteSpace(content[i]))
                i++;

            // Parse value
            var (value, newIndex) = ParseValue(content, i);
            result[key] = value;
            i = newIndex;
        }
    }

    private static (object value, int nextIndex) ParseValue(string s, int start)
    {
        var i = start;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;

        if (i >= s.Length)
            return (null, i);

        var c = s[i];

        if (c == '"')
        {
            // String
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(s[i]); break;
                    }
                    i++;
                }
                else if (s[i] == '"')
                {
                    i++;
                    break;
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            return (sb.ToString(), i);
        }

        if (c == '{')
        {
            // Object
            var braceCount = 1;
            var objStart = i;
            i++;
            while (i < s.Length && braceCount > 0)
            {
                if (s[i] == '{') braceCount++;
                else if (s[i] == '}') braceCount--;
                i++;
            }
            var objStr = s.Substring(objStart, i - objStart);
            var dict = new Dictionary<string, object>();
            ParseObject(objStr.Substring(1, objStr.Length - 2), dict);
            return (dict, i);
        }

        if (c == '[')
        {
            // Array - simplified, treat as list of values
            var list = new List<object>();
            i++;
            while (i < s.Length)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                    i++;
                if (i >= s.Length || s[i] == ']')
                {
                    i++;
                    break;
                }
                var (val, newIdx) = ParseValue(s, i);
                list.Add(val);
                i = newIdx;
            }
            return (list, i);
        }

        // Number or boolean
        var numStart = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',' && s[i] != '}' && s[i] != ']')
            i++;

        var numStr = s.Substring(numStart, i - numStart).Trim();

        if (numStr == "true") return (true, i);
        if (numStr == "false") return (false, i);
        if (numStr == "null") return (null, i);

        if (System.Numerics.BigInteger.TryParse(numStr, out var bi))
            return (bi, i);

        if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return (d, i);

        return (numStr, i);
    }

}
#endif
