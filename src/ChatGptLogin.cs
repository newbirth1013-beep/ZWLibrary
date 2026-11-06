using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace ZWLibrary
{
    public sealed class ChatGptSession
    {
        public string ClientId { get; set; }
        public string Subject { get; set; }
        public string Email { get; set; }
        public string IdToken { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string Scope { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }
    public sealed class ChatGptModel
    {
        public string Slug { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }
    internal static class ChatGptLogin
    {
        private const string Auth = "https://auth.openai.com";
        private const string Resource = "https://api.openai.com/v1";
        private const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        // Each process locks the protected record during credential changes and refresh.
        internal static string StorageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZWLibrary", "ChatGPT");
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2000000 };
        private static string SessionPath { get { return Path.Combine(StorageRoot, "session.dpapi"); } }
        private static FileStream FileLock()
        {
            Directory.CreateDirectory(StorageRoot);
            try { return new FileStream(Path.Combine(StorageRoot, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("다른 ZWCAD에서 로그인 정보를 사용 중입니다. 잠시 후 다시 시도하세요."); }
        }
        internal static ChatGptSession Load()
        {
            if (!File.Exists(SessionPath)) return null;
            try { return Json.Deserialize<ChatGptSession>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), null, DataProtectionScope.CurrentUser))); }
            catch { throw new InvalidOperationException("저장된 로그인 정보를 읽을 수 없습니다. 로그아웃 후 다시 로그인하세요."); }
        }
        internal static void Save(ChatGptSession session)
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Serialize(session)), null, DataProtectionScope.CurrentUser);
            string temp = Path.Combine(StorageRoot, Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
                if (File.Exists(SessionPath)) File.Replace(temp, SessionPath, null); else File.Move(temp, SessionPath);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static string B64(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        internal static byte[] UnB64(string text) { text = text.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '=')); }
        private static string RandomValue() { var b = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b); return B64(b); }
        private static string HostId()
        {
            string path = Path.Combine(StorageRoot, "host.txt");
            if (File.Exists(path)) { string stored = File.ReadAllText(path); Guid parsed; if (stored.StartsWith("urn:uuid:") && Guid.TryParse(stored.Substring(9), out parsed)) return stored; throw new InvalidOperationException("호스트 식별자가 손상되었습니다."); }
            string host = "urn:uuid:" + Guid.NewGuid().ToString(); File.WriteAllText(path, host); return host;
        }
        internal static string Get(Dictionary<string, object> map, string key)
        { object value; return map.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
        internal static void CheckScopes(string scopes)
        {
            var granted = (scopes ?? "").Split(' ');
            if (!granted.Contains("chatgpt.tokens.use.direct") || !granted.Contains("resource.invoke")) throw new InvalidOperationException("ChatGPT 구독 사용 권한이 승인되지 않았습니다. 지원 플랜과 앱 접근 권한을 확인하세요.");
        }
        internal static HttpClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
        }
        private static async Task<Dictionary<string, object>> Token(Dictionary<string, string> fields, CancellationToken ct)
        {
            using (var client = Client()) using (var body = new FormUrlEncodedContent(fields))
            using (var response = await client.PostAsync(Auth + "/api/accounts/oauth/token", body, ct)) {
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("ChatGPT 인증 실패 (HTTP " + (int)response.StatusCode + "). 새 로그인으로 다시 시도하세요.");
                return Json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
            }
        }
        internal static async Task<ChatGptSession> SignIn(CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try { using (FileLock()) {
                var previous = Load();
                string clientId = previous == null ? "dynamic_agent_client" : previous.ClientId;
                string host = HostId(), state = RandomValue(), nonce = RandomValue(), verifier = RandomValue(), challenge;
                using (var sha = SHA256.Create()) challenge = B64(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                // TcpListener binds loopback directly; no HTTP.sys URL reservation or administrator rights.
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                try {
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    string redirect = "http://127.0.0.1:" + port + "/auth/callback";
                    var fields = new Dictionary<string, string> { { "client_id", clientId }, { "ext_agent_host_id", host }, { "response_type", "code" }, { "redirect_uri", redirect }, { "scope", Scopes }, { "resource", Resource }, { "state", state }, { "nonce", nonce }, { "code_challenge_method", "S256" }, { "code_challenge", challenge } };
                    if (previous == null) fields["agent_name_hint"] = "ZWLibrary";
                    else if (!string.IsNullOrEmpty(previous.IdToken)) fields["id_token_hint"] = previous.IdToken;
                    string url = Auth + "/api/accounts/authorize?" + string.Join("&", fields.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                        timeout.CancelAfter(TimeSpan.FromMinutes(5));
                        using (timeout.Token.Register(() => listener.Stop())) {
                            while (true) {
                                TcpClient connection;
                                try { connection = await listener.AcceptTcpClientAsync(); } catch (Exception) { timeout.Token.ThrowIfCancellationRequested(); throw; }
                                using (connection) using (timeout.Token.Register(() => connection.Close())) using (var stream = connection.GetStream()) {
                                    var bytes = new List<byte>(); var one = new byte[1];
                                    while (bytes.Count < 16384) {
                                        int read = await stream.ReadAsync(one, 0, 1, timeout.Token); if (read == 0 || one[0] == 10) break; bytes.Add(one[0]);
                                    }
                                    string line = Encoding.ASCII.GetString(bytes.ToArray()).Trim();
                                    var parts = line.Split(' '); Uri callback;
                                    bool route = parts.Length == 3 && parts[0] == "GET" && parts[1].StartsWith("/auth/callback?") && Uri.TryCreate(redirect.Substring(0, redirect.IndexOf("/auth/callback")) + parts[1], UriKind.Absolute, out callback);
                                    var query = route ? HttpUtility.ParseQueryString(new Uri("http://127.0.0.1" + parts[1]).Query) : null;
                                    bool valid = route && query.GetValues("state") != null && query.GetValues("state").Length == 1 && query["state"] == state;
                                    string html = valid ? "Login received. Return to ZWCAD to check the result." : "Invalid callback.";
                                    byte[] payload = Encoding.UTF8.GetBytes(html);
                                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + (valid ? "200 OK" : "400 Bad Request") + "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + payload.Length + "\r\n\r\n");
                                    await stream.WriteAsync(header, 0, header.Length, timeout.Token); await stream.WriteAsync(payload, 0, payload.Length, timeout.Token);
                                    if (!valid) continue;
                                    if (!string.IsNullOrEmpty(query["error"])) throw new InvalidOperationException("로그인이 취소되었거나 권한이 승인되지 않았습니다.");
                                    string issued = query["client_id"];
                                    if (previous == null) { if (string.IsNullOrWhiteSpace(issued) || issued == "dynamic_agent_client") throw new InvalidOperationException("앱 등록이 완료되지 않았습니다."); clientId = issued; }
                                    else if (!string.IsNullOrEmpty(issued) && issued != clientId) throw new InvalidOperationException("로그인 앱 식별자가 일치하지 않습니다.");
                                    if (query.GetValues("code") == null || query.GetValues("code").Length != 1 || string.IsNullOrWhiteSpace(query["code"])) throw new InvalidOperationException("인증 코드가 없습니다.");
                                    var tokens = await Token(new Dictionary<string, string> { { "grant_type", "authorization_code" }, { "client_id", clientId }, { "code", query["code"] }, { "code_verifier", verifier }, { "redirect_uri", redirect }, { "resource", Resource } }, timeout.Token);
                                    var claims = await ValidateIdentity(Get(tokens, "id_token"), clientId, nonce, timeout.Token);
                                    if (previous != null && previous.Subject != Get(claims, "sub")) throw new InvalidOperationException("기존 계정과 다른 계정입니다. 로그아웃 후 계정을 변경하세요.");
                                    CheckScopes(Get(tokens, "scope"));
                                    var session = new ChatGptSession { ClientId = clientId, Subject = Get(claims, "sub"), Email = Get(claims, "email"), IdToken = Get(tokens, "id_token") };
                                    UpdateTokens(session, tokens); Save(session); return session;
                                }
                            }
                        }
                    }
                } finally { listener.Stop(); }
            } } finally { Gate.Release(); }
        }
        internal static Dictionary<string, object> ValidateJwt(string jwt, string clientId, string nonce, Dictionary<string, object> jwks, DateTime now)
        {
            string[] sections = jwt.Split('.'); if (sections.Length != 3) throw new InvalidOperationException("ID 토큰 형식 오류");
            var header = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(UnB64(sections[0])));
            if (Get(header, "alg") != "RS256" || string.IsNullOrEmpty(Get(header, "kid"))) throw new InvalidOperationException("지원하지 않는 ID 토큰 서명");
            bool verified = false;
            foreach (Dictionary<string, object> key in (IEnumerable)jwks["keys"]) {
                if (Get(key, "kid") != Get(header, "kid") || Get(key, "kty") != "RSA" || (Get(key, "use") != "" && Get(key, "use") != "sig") || (Get(key, "alg") != "" && Get(key, "alg") != "RS256")) continue;
                using (var rsa = new RSACryptoServiceProvider()) { rsa.PersistKeyInCsp = false; rsa.ImportParameters(new RSAParameters { Modulus = UnB64(Get(key, "n")), Exponent = UnB64(Get(key, "e")) });
                    verified = rsa.VerifyData(Encoding.ASCII.GetBytes(sections[0] + "." + sections[1]), CryptoConfig.MapNameToOID("SHA256"), UnB64(sections[2])); }
            }
            if (!verified) throw new InvalidOperationException("ID 토큰 서명 확인 실패");
            var claims = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(UnB64(sections[1])));
            object audience; bool matches = false;
            if (claims.TryGetValue("aud", out audience)) matches = audience is string ? (string)audience == clientId : ((IEnumerable)audience).Cast<object>().Any(a => Convert.ToString(a) == clientId);
            long unix = (long)(now.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            if (Get(claims, "iss") != Auth || !matches || Get(claims, "nonce") != nonce || string.IsNullOrWhiteSpace(Get(claims, "sub")) || !claims.ContainsKey("exp") || !claims.ContainsKey("iat") || Convert.ToInt64(claims["exp"]) <= unix - 5 || Convert.ToInt64(claims["iat"]) > unix + 5 || (claims.ContainsKey("nbf") && Convert.ToInt64(claims["nbf"]) > unix + 5)) throw new InvalidOperationException("ID 토큰의 계정/유효기간/로그인 요청이 일치하지 않습니다.");
            if (claims.ContainsKey("azp") && Get(claims, "azp") != clientId) throw new InvalidOperationException("ID 토큰 승인 대상 오류");
            return claims;
        }
        private static async Task<Dictionary<string, object>> ValidateIdentity(string jwt, string clientId, string nonce, CancellationToken ct)
        {
            using (var client = Client()) using (var response = await client.GetAsync(Auth + "/.well-known/jwks.json", ct)) {
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("로그인 서명 키 조회 실패");
                return ValidateJwt(jwt, clientId, nonce, Json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync()), DateTime.UtcNow);
            }
        }
        private static void UpdateTokens(ChatGptSession session, Dictionary<string, object> tokens)
        {
            if (Get(tokens, "token_type").ToLowerInvariant() != "bearer" || string.IsNullOrWhiteSpace(Get(tokens, "access_token"))) throw new InvalidOperationException("사용 가능한 인증 토큰이 없습니다.");
            if (tokens.ContainsKey("scope")) session.Scope = Get(tokens, "scope");
            CheckScopes(session.Scope);
            double seconds; if (!double.TryParse(Get(tokens, "expires_in"), out seconds) || seconds <= 0 || seconds > 604800) throw new InvalidOperationException("토큰 유효기간 오류");
            session.AccessToken = Get(tokens, "access_token"); session.ExpiresUtc = DateTime.UtcNow.AddSeconds(seconds);
            if (!string.IsNullOrEmpty(Get(tokens, "refresh_token"))) session.RefreshToken = Get(tokens, "refresh_token");
        }
        internal static async Task<string> AccessToken(CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try { using (FileLock()) {
                var session = Load(); if (session == null || string.IsNullOrEmpty(session.AccessToken)) throw new InvalidOperationException("Continue with ChatGPT 버튼으로 먼저 로그인하세요.");
                CheckScopes(session.Scope);
                if (session.ExpiresUtc <= DateTime.UtcNow.AddMinutes(2)) {
                    if (string.IsNullOrEmpty(session.RefreshToken)) throw new InvalidOperationException("로그인이 만료되었습니다. 다시 로그인하세요.");
                    var tokens = await Token(new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "client_id", session.ClientId }, { "refresh_token", session.RefreshToken }, { "resource", Resource } }, ct);
                    UpdateTokens(session, tokens); Save(session);
                }
                return session.AccessToken;
            } } finally { Gate.Release(); }
        }
        internal static async Task<List<ChatGptModel>> Models(CancellationToken ct)
        {
            string token = await AccessToken(ct);
            using (var client = Client()) {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await client.GetAsync(Resource + "/models", ct)) {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(AgentApi.DescribeError((int)response.StatusCode, await response.Content.ReadAsStringAsync()));
                    return ParseModels(await response.Content.ReadAsStringAsync());
                }
            }
        }
        internal static List<ChatGptModel> ParseModels(string payload)
        {
            var root = Json.Deserialize<Dictionary<string, object>>(payload); var result = new List<ChatGptModel>();
            object models;
            if (!root.TryGetValue("models", out models)) throw new InvalidOperationException("ChatGPT 모델 목록 형식이 다릅니다.");
            foreach (Dictionary<string, object> model in (IEnumerable)models) if (Get(model, "visibility") == "list" && !string.IsNullOrWhiteSpace(Get(model, "slug"))) result.Add(new ChatGptModel { Slug = Get(model, "slug"), Name = Get(model, "display_name") });
            return result;
        }
        internal static async Task<string> SignOut(CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try { using (FileLock()) {
                var session = Load(); bool revoked = session == null || string.IsNullOrEmpty(session.RefreshToken);
                try { if (!revoked) using (var client = Client()) {
                    var discovery = Json.Deserialize<Dictionary<string, object>>(await client.GetStringAsync(Auth + "/.well-known/openid-configuration"));
                    Uri endpoint; if (!Uri.TryCreate(Get(discovery, "revocation_endpoint"), UriKind.Absolute, out endpoint) || endpoint.Scheme != "https" || endpoint.Host != "auth.openai.com" || !endpoint.IsDefaultPort || endpoint.UserInfo != "") throw new InvalidOperationException();
                    using (var body = new FormUrlEncodedContent(new Dictionary<string, string> { { "token", session.RefreshToken }, { "token_type_hint", "refresh_token" }, { "client_id", session.ClientId } }))
                    using (var response = await client.PostAsync(endpoint, body, ct)) revoked = response.StatusCode == HttpStatusCode.OK;
                } } catch { revoked = false; }
                if (session != null) { session.AccessToken = session.RefreshToken = session.IdToken = ""; session.ExpiresUtc = DateTime.MinValue; Save(session); }
                return revoked ? "로그아웃했습니다." : "PC에서는 로그아웃했습니다. 원격 해제를 확인하지 못했습니다. ChatGPT 설정에서 ZWLibrary 연결을 해제하세요.";
            } } finally { Gate.Release(); }
        }
    }
}
