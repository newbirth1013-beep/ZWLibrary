using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using ZWLibrary;
class AgentTests {
 static int n;
 static void Check(bool b) { if(!b) throw new Exception("assertion " + n); n++; }
 static void Reject(Action a) { try { a(); } catch { n++; return; } throw new Exception("expected rejection"); }
 static void Main() {
  var p = AgentApi.Parse("{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\"},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"action\\\":\\\"rename\\\",\\\"handle\\\":\\\"AB\\\",\\\"old_tag\\\":\\\"CB01\\\",\\\"new_tag\\\":\\\"CB02\\\",\\\"explanation\\\":\\\"변경 제안\\\"}\"}]}]}");
  Check(p.action=="rename" && p.new_tag=="CB02");
  Reject(()=>AgentApi.Parse("{\"status\":\"incomplete\",\"output\":[]}"));
  Reject(()=>AgentApi.Parse("{\"status\":\"completed\",\"output\":[{\"content\":[{\"type\":\"refusal\"}]}]}"));
  Reject(()=>new AgentPlan {action="delete"}.Validate());
  Reject(()=>new AgentPlan {action="rename",handle="AB",new_tag=""}.Validate());
  Reject(()=>new AgentPlan {action="rename",handle="AB",new_tag="CB\n02"}.Validate());
  Reject(()=>new AgentPlan {action="rename",handle="AB",new_tag=new string('x',201)}.Validate());
  new AgentPlan {action="explain"}.Validate(); n++;
  Check(AgentApi.DescribeError(429, "{\"error\":{\"type\":\"insufficient_quota\",\"code\":\"credit_balance_exhausted\"}}").Contains("반복 전송"));
  Check(AgentApi.DescribeError(429, "{\"error\":{\"type\":\"rate_limit_error\",\"code\":\"slow_down\"}}").Contains("속도 제한"));
  Check(AgentApi.DescribeError(429, "<html>bad gateway</html>").Contains("구분할 수 없"));
  Check(!AgentApi.DescribeError(401, "{\"error\":{\"message\":\"sk-secret\"}}").Contains("sk-secret"));
  Check(AgentApi.DescribeError(404, "{}").Contains("모델 ID"));
  Check(AgentApi.ParseStreamEvent("{\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}") == null);
  Reject(()=>AgentApi.ParseStreamEvent("{\"type\":\"response.incomplete\"}"));
  Reject(()=>AgentApi.ParseStreamEvent("{\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_unavailable\"}}}"));
  Check(AgentApi.DescribeError(400,"{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}").Contains("자동 전환하지"));
  Reject(()=>ChatGptLogin.CheckScopes("openid profile email"));
  ChatGptLogin.CheckScopes("resource.invoke chatgpt.tokens.use.direct"); n++;
  var models = ChatGptLogin.ParseModels("{\"models\":[{\"visibility\":\"hidden\",\"slug\":\"hidden\"},{\"visibility\":\"list\",\"slug\":\"first\",\"display_name\":\"First model\"},{\"visibility\":\"list\",\"slug\":\"second\",\"display_name\":\"Second model\"}]}");
  Check(models.Count == 2 && models[0].Slug == "first" && models[1].Slug == "second");
  Reject(()=>ChatGptLogin.ParseModels("{\"data\":[]}"));
  using(var rsa = new RSACryptoServiceProvider(2048)) {
   rsa.PersistKeyInCsp=false;
   var key=rsa.ExportParameters(false);
   var jwks=new Dictionary<string,object> { {"keys",new object[] {new Dictionary<string,object>{{"kid","fixture"},{"kty","RSA"},{"n",ChatGptLogin.B64(key.Modulus)},{"e",ChatGptLogin.B64(key.Exponent)}}}} };
   var now=DateTime.UtcNow; long unix=(long)(now-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
   var claims=new Dictionary<string,object>{{"iss","https://auth.openai.com"},{"aud",new[]{"app"}},{"nonce","nonce"},{"sub","subject"},{"iat",unix},{"exp",unix+3600}};
   string jwt=Signed(rsa,claims);
   Check(ChatGptLogin.ValidateJwt(jwt,"app","nonce",jwks,now)["sub"].ToString()=="subject");
   Reject(()=>ChatGptLogin.ValidateJwt(jwt,"other-app","nonce",jwks,now));
   Reject(()=>ChatGptLogin.ValidateJwt(jwt,"app","wrong-nonce",jwks,now));
   Reject(()=>ChatGptLogin.ValidateJwt(jwt,"app","nonce",jwks,now.AddHours(2)));
   var sections=jwt.Split('.'); sections[1]=ChatGptLogin.B64(Encoding.UTF8.GetBytes("{}"));
   Reject(()=>ChatGptLogin.ValidateJwt(string.Join(".",sections),"app","nonce",jwks,now));
   claims["iss"]="https://example.com";
   Reject(()=>ChatGptLogin.ValidateJwt(Signed(rsa,claims),"app","nonce",jwks,now));
  }
  string testRoot=Path.Combine(Path.GetFullPath("output"),"agent-tests","credentials-"+Guid.NewGuid().ToString("N"));
  ChatGptLogin.StorageRoot=testRoot; Directory.CreateDirectory(testRoot);
  try {
   ChatGptLogin.Save(new ChatGptSession {ClientId="fixture",Subject="subject",AccessToken="test-secret-never-live"});
   Check(ChatGptLogin.Load().AccessToken=="test-secret-never-live");
   Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(testRoot,"session.dpapi"))).Contains("test-secret-never-live"));
   ChatGptLogin.Save(new ChatGptSession {ClientId="fixture",Subject="subject",AccessToken="rotated"});
   Check(ChatGptLogin.Load().AccessToken=="rotated");
  } finally { foreach(var file in Directory.GetFiles(testRoot)) File.Delete(file); Directory.Delete(testRoot); }
  var outgoing=AgentApi.Json.Deserialize<Dictionary<string,object>>(AgentApi.Json.Serialize(AgentApi.BuildRequest("test-model","test-request","test-context")));
  Check(!(outgoing["input"] is string) && outgoing["input"] is System.Collections.IEnumerable);
  Check((bool)outgoing["stream"] && !(bool)outgoing["store"]);
  var messages=((System.Collections.IEnumerable)outgoing["input"]).GetEnumerator();Check(messages.MoveNext());
  Check(((Dictionary<string,object>)messages.Current)["role"].ToString()=="user");
  var state=new AgentApi.StreamState();
  string done=AgentApi.Json.Serialize(new { type="response.output_text.done", item_id="msg", content_index=0, text=AgentApi.Json.Serialize(new AgentPlan {action="explain",explanation="연결 확인 성공"}) });
  Check(state.Accept(done)==null);
  Check(state.Accept("{\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}").action=="explain");
  var interrupted=new AgentApi.StreamState();interrupted.Accept(done);
  Reject(()=>interrupted.Accept("{\"type\":\"response.incomplete\"}"));
  var refusal=new AgentApi.StreamState();refusal.Accept(done);refusal.Accept("{\"type\":\"response.refusal.done\"}");
  Reject(()=>refusal.Accept("{\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}"));
  Reject(()=>new AgentApi.StreamState().Accept("{\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}"));
  Console.WriteLine("Agent tests PASS: "+n);
 }
 static string Signed(RSACryptoServiceProvider rsa,Dictionary<string,object> claims) {
  string body=ChatGptLogin.B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"fixture\"}"))+"."+ChatGptLogin.B64(Encoding.UTF8.GetBytes(ChatGptLogin.Json.Serialize(claims)));
  return body+"."+ChatGptLogin.B64(rsa.SignData(Encoding.ASCII.GetBytes(body),CryptoConfig.MapNameToOID("SHA256")));
 }
}
