using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EmueraWebPackager;

// In-process listener: Stop cannot affect any other process or localhost service.
public sealed class PreviewServer : IDisposable
{
    readonly TcpListener listener;
    readonly string root;
    readonly CancellationTokenSource stopping = new();
    readonly SemaphoreSlim connections = new(8);
    public string Url { get; }
    public PreviewServer(string webRoot)
    {
        root = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(root, "index.html"))) throw new IOException("index.htmlがありません");
        listener = new(IPAddress.Loopback, 0); listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        _ = Listen();
    }
    async Task Listen()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await connections.WaitAsync(stopping.Token);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stopping.Token); }
                catch { connections.Release(); throw; }
                _ = Serve(client);
            }
        }
        catch (Exception ex) when (stopping.IsCancellationRequested && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    async Task Serve(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30)); var cancel = timeout.Token;
            try
            {
                using NetworkStream stream = client.GetStream(); var header = new StringBuilder(); byte[] buffer = new byte[1];
                while (header.Length < 16384 && !header.ToString().EndsWith("\r\n\r\n"))
                {
                    if (await stream.ReadAsync(buffer, cancel) == 0) return; header.Append((char)buffer[0]);
                }
                string[] lines = header.ToString().Split("\r\n"); string[] request = lines[0].Split(' ');
                async Task Reply(int status, string label) => await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {label}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cancel);
                if (header.Length >= 16384 || request.Length != 3) { await Reply(400, "Bad Request"); return; }
                if (request[0] is not "GET" and not "HEAD") { await Reply(405, "Method Not Allowed"); return; }
                string relative = Uri.UnescapeDataString(request[1].Split('?')[0]).TrimStart('/');
                if (relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(p => p is "." or "..") || relative.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) { await Reply(403, "Forbidden"); return; }
                string file = Path.GetFullPath(Path.Combine(root, relative.Length == 0 ? "index.html" : relative));
                if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { await Reply(403, "Forbidden"); return; }
                if (!File.Exists(file) && !Path.HasExtension(relative)) file = Path.Combine(root, "index.html");
                if (!File.Exists(file)) { await Reply(404, "Not Found"); return; }
                for (var dir = new DirectoryInfo(Path.GetDirectoryName(file)!); dir != null && dir.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase); dir = dir.Parent)
                    if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) { await Reply(403, "Forbidden"); return; }
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) { await Reply(403, "Forbidden"); return; }
                string extension = Path.GetExtension(file).ToLowerInvariant(); string mime = extension switch
                {
                    ".html" => "text/html; charset=utf-8", ".js" => "application/javascript", ".css" => "text/css", ".json" => "application/json", ".wasm" => "application/wasm", ".zip" => "application/zip", ".png" => "image/png", ".webp" => "image/webp", ".svg" => "image/svg+xml", ".ico" => "image/x-icon", _ => "application/octet-stream"
                };
                string accept = lines.FirstOrDefault(l => l.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase)) ?? "";
                string encoding = "";
                if (accept.Contains("br") && File.Exists(file + ".br")) { file += ".br"; encoding = "Content-Encoding: br\r\n"; }
                else if (accept.Contains("gzip") && File.Exists(file + ".gz")) { file += ".gz"; encoding = "Content-Encoding: gzip\r\n"; }
                using var input = File.OpenRead(file); long start = 0, end = input.Length - 1; string rangeHeader = "";
                string? range = lines.FirstOrDefault(l => l.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase));
                if (range != null)
                {
                    string[] bounds = range[13..].Split('-');
                    if (bounds.Length != 2 || !long.TryParse(bounds[0], out start) || start < 0 || start >= input.Length || (bounds[1].Length > 0 && !long.TryParse(bounds[1], out end)) || end < start) { await Reply(416, "Range Not Satisfiable"); return; }
                    end = Math.Min(end, input.Length - 1); rangeHeader = $"Content-Range: bytes {start}-{end}/{input.Length}\r\n";
                }
                string headers = $"HTTP/1.1 {(range != null ? "206 Partial Content" : "200 OK")}\r\nContent-Type: {mime}\r\nContent-Length: {end - start + 1}\r\n{encoding}{rangeHeader}Cache-Control: no-cache\r\nAccept-Ranges: bytes\r\nVary: Accept-Encoding\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancel);
                if (request[0] == "HEAD") return;
                input.Position = start; long remaining = end - start + 1; byte[] bytes = new byte[65536];
                while (remaining > 0) { int n = await input.ReadAsync(bytes.AsMemory(0, (int)Math.Min(bytes.Length, remaining)), cancel); if (n == 0) break; await stream.WriteAsync(bytes.AsMemory(0, n), cancel); remaining -= n; }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ArgumentException) { }
            finally { connections.Release(); }
        }
    }
    public void Dispose() { stopping.Cancel(); listener.Stop(); }
}
