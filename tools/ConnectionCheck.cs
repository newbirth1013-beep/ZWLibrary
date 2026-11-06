using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ZWLibrary;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class ConnectionCheck : Form
{
 readonly Button login = new Button {Text="Continue with ChatGPT",Dock=DockStyle.Top,Height=44};
 readonly Button test = new Button {Text="선택 모델로 연결 확인",Dock=DockStyle.Top,Height=40,Enabled=false};
 readonly ComboBox models = new ComboBox {Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
 readonly TextBox status = new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
 readonly CancellationTokenSource cancel = new CancellationTokenSource();
 readonly string statePath;
 ConnectionCheck(string path,bool start)
 {
  statePath=path; Text="ZWLibrary — ChatGPT 연결 시험"; Width=720; Height=420; Font=new Font("Malgun Gothic",10); StartPosition=FormStartPosition.CenterScreen;
  Controls.Add(status); Controls.Add(test); Controls.Add(models); Controls.Add(login);
  login.Click+=async delegate {
   login.Enabled=test.Enabled=false; models.Items.Clear();
   try {
    State("opening_browser","브라우저에서 ChatGPT 로그인과 사용 권한 동의를 완료해주세요.\r\n비밀번호는 이 프로그램에서 입력받지 않습니다.");
    await ChatGptLogin.SignIn(cancel.Token);
    State("signed_in","로그인 확인. 사용 가능한 모델을 조회합니다.");
    var list=await ChatGptLogin.Models(cancel.Token);
    if(IsDisposed)return;
    foreach(var item in list)models.Items.Add(item);
    if(models.Items.Count==0)throw new InvalidOperationException("계정에서 표시 가능한 모델이 없습니다.");
    models.SelectedIndex=0;test.Enabled=true;
    State("models_ready","로그인 및 모델 목록 조회 성공. 모델을 선택하고 연결 확인을 누르세요.\r\n이 시험은 도면을 읽거나 수정하지 않습니다.");
   } catch(OperationCanceledException){State("cancelled","로그인이 취소되었거나 시간이 초과됐습니다.");}
     catch(Exception ex){State("failed",ex.GetType().Name + ": " + ex.Message + (ex.InnerException == null ? "" : "\r\n" + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message));}
   finally{if(!IsDisposed)login.Enabled=true;}
  };
  test.Click+=async delegate {
   var selected=models.SelectedItem as ChatGptModel;if(selected==null)return;
   login.Enabled=test.Enabled=models.Enabled=false;
   try {
    State("testing","ChatGPT 구독으로 짧은 연결 확인 요청을 보냅니다. 도면 정보는 전송하지 않습니다.");
    var response=await AgentApi.Ask(selected.Slug,"연결 시험입니다. 작업 변경 없이 연결 확인 성공이라고 한국어로 설명하세요.","시험용 정보: 관리 부품 0개. 실제 도면 데이터 없음.");
    State("request_succeeded","로그인·모델 조회·AI 응답 완료 확인 성공.\r\n"+response.explanation+"\r\n이제 ZWCAD에서 같은 로그인 정보로 시험할 수 있습니다.");
   } catch(Exception ex){State("failed",ex.GetType().Name + ": " + ex.Message + (ex.InnerException == null ? "" : "\r\n" + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message));}
   finally{if(!IsDisposed)login.Enabled=test.Enabled=models.Enabled=true;}
  };
  FormClosing+=delegate {cancel.Cancel();};
  Shown+=delegate {State("ready","ChatGPT 로그인 연결 시험. API 키나 별도 API 결제 없이 로그인 방식을 확인합니다."); if(start)login.PerformClick();};
 }
 void State(string phase,string message)
 {
  // Only non-secret status is written. Tokens and account identifiers are never logged.
  try {File.WriteAllText(statePath,phase+Environment.NewLine+message);}catch{}
  if(!IsDisposed)status.Text=message;
 }
 [STAThread]static void Main(string[] args)
 {
  if(args.Length>1 && args[1]=="--probe") {
   try {
    System.Threading.Tasks.Task.Run(async delegate {
     var list=await ChatGptLogin.Models(CancellationToken.None);
     if(list.Count==0)throw new InvalidOperationException("계정에서 사용할 모델이 없습니다.");
     File.WriteAllText(Path.GetFullPath(args[0]),"testing"+Environment.NewLine+"모델: "+list[0].Slug);
     var answer=await AgentApi.Ask(list[0].Slug,"연결 시험입니다. 작업 변경 없이 연결 확인 성공이라고 한국어로 설명하세요.","시험용 정보: 관리 부품 0개. 실제 도면 데이터 없음.");
     File.WriteAllText(Path.GetFullPath(args[0]),"request_succeeded"+Environment.NewLine+"모델: "+list[0].Slug+Environment.NewLine+answer.explanation);
    }).GetAwaiter().GetResult();
   }catch(Exception ex){File.WriteAllText(Path.GetFullPath(args[0]),"failed"+Environment.NewLine+ex.GetType().Name+": "+ex.Message+(ex.InnerException==null?"":Environment.NewLine+ex.InnerException.GetType().Name+": "+ex.InnerException.Message));}
   return;
  }
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  string path=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"connection-status.txt");
  using(var form=new ConnectionCheck(path,args.Length>1&&args[1]=="--login"))Application.Run(form);
 }
}
