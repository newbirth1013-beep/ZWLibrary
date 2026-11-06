using System;
using System.Windows.Forms;
using System.Threading;
namespace ZWLibrary
{
    internal sealed class AgentDialog : Form
    {
        private readonly TextBox request = new TextBox { Multiline = true, Height = 75, Dock = DockStyle.Top };
        private readonly ComboBox model = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button login = new Button { Text = "Continue with ChatGPT", Dock = DockStyle.Top, Height = 38 };
        private readonly Button logout = new Button { Text = "로그아웃", Dock = DockStyle.Top, Height = 30 };
        private readonly Button cancel = new Button { Text = "로그인 취소", Dock = DockStyle.Bottom, Height = 30 };
        private CancellationTokenSource loginCancellation;
        protected override void OnFormClosing(FormClosingEventArgs e) { if (loginCancellation != null) loginCancellation.Cancel(); base.OnFormClosing(e); }
        private readonly TextBox result = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill };
        private readonly Button ask = new Button { Text = "선택 부품 정보를 전송하고 AI 요청", Dock = DockStyle.Bottom, Height = 38 };
        private readonly Button apply = new Button { Text = "표시된 번호 변경 적용", Dock = DockStyle.Bottom, Height = 38, Enabled = false };
        internal AgentPlan Plan;
        internal AgentDialog(string context)
        {
            Text = "ZWLibrary AI — 개발 미리보기"; Width = 850; Height = 650; StartPosition = FormStartPosition.CenterParent;
            Font = new System.Drawing.Font("Malgun Gothic", 9F);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 42, 38, 30, 22, 30, 22, 75 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 40, 40, 32, 2 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(root);
            root.Controls.Add(new Label { Text = "ChatGPT 구독으로 AI 사용 · API 키 불필요\r\n전송 버튼을 누르면 선택 부품 정보와 요청이 OpenAI로 전송됩니다.", Dock = DockStyle.Fill }, 0, 0);
            root.Controls.Add(login, 0, 1); root.Controls.Add(logout, 0, 2);
            root.Controls.Add(new Label { Text = "사용할 모델 (로그인 후 목록에서 선택)", Dock = DockStyle.Fill }, 0, 3);
            root.Controls.Add(model, 0, 4);
            root.Controls.Add(new Label { Text = "요청 — 예: 선택한 부품의 번호를 CB03으로 변경해줘", Dock = DockStyle.Fill }, 0, 5);
            request.Dock = DockStyle.Fill; root.Controls.Add(request, 0, 6); root.Controls.Add(result, 0, 7);
            root.Controls.Add(ask, 0, 8); root.Controls.Add(apply, 0, 9); root.Controls.Add(cancel, 0, 10);
            result.Text = context; result.Select(0, 0); cancel.Enabled = false;
            request.TextChanged += delegate { Plan = null; apply.Enabled = false; };
            model.SelectedIndexChanged += delegate { Plan = null; apply.Enabled = false; };
            cancel.Click += delegate { if (loginCancellation != null) loginCancellation.Cancel(); };
            login.Click += async delegate {
                Plan = null; apply.Enabled = false; ask.Enabled = login.Enabled = logout.Enabled = false;
                loginCancellation = new CancellationTokenSource(); cancel.Enabled = true;
                try {
                    var session = await ChatGptLogin.SignIn(loginCancellation.Token);
                    var choices = await ChatGptLogin.Models(loginCancellation.Token);
                    if (IsDisposed) return;
                    model.Items.Clear(); foreach (var choice in choices) model.Items.Add(choice);
                    if (model.Items.Count > 0) model.SelectedIndex = 0;
                    result.Text = context + "\r\n\r\n로그인: " + session.Email + "\r\n모델 " + choices.Count + "개. 첫 AI 응답 성공 시 실제 모델 접근이 확인됩니다.";
                } catch (OperationCanceledException) { if (!IsDisposed) result.Text = context + "\r\n로그인을 취소했습니다."; }
                  catch (System.Exception ex) { if (!IsDisposed) result.Text = context + "\r\n" + ex.Message; }
                finally { loginCancellation.Dispose(); loginCancellation = null; if (!IsDisposed) { ask.Enabled = login.Enabled = logout.Enabled = true; cancel.Enabled = false; } }
            };
            logout.Click += async delegate {
                Plan = null; apply.Enabled = false; model.Items.Clear(); ask.Enabled = login.Enabled = logout.Enabled = false;
                try { var message = await ChatGptLogin.SignOut(CancellationToken.None); if (!IsDisposed) result.Text = context + "\r\n" + message; }
                catch (System.Exception ex) { if (!IsDisposed) result.Text = context + "\r\n" + ex.Message; }
                finally { if (!IsDisposed) ask.Enabled = login.Enabled = logout.Enabled = true; }
            };
            Shown += async delegate {
                ask.Enabled = login.Enabled = logout.Enabled = false;
                try {
                    var saved = ChatGptLogin.Load();
                    if (saved != null && !string.IsNullOrEmpty(saved.AccessToken)) {
                        var choices = await ChatGptLogin.Models(CancellationToken.None);
                        if (IsDisposed) return;
                        foreach (var choice in choices) model.Items.Add(choice); if (model.Items.Count > 0) model.SelectedIndex = 0;
                        result.Text = context + "\r\n로그인: " + saved.Email;
                    }
                } catch (System.Exception ex) { if (!IsDisposed) result.Text = context + "\r\n" + ex.Message; }
                finally { if (!IsDisposed) ask.Enabled = login.Enabled = logout.Enabled = true; }
            };
            apply.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            ask.Click += async delegate {
                if (string.IsNullOrWhiteSpace(request.Text)) return;
                Plan = null; apply.Enabled = false; ask.Enabled = login.Enabled = logout.Enabled = model.Enabled = false; request.ReadOnly = true;
                try {
                    var selectedModel = model.SelectedItem as ChatGptModel;
                    var p = await AgentApi.Ask(selectedModel == null ? "" : selectedModel.Slug, request.Text, context);
                    if (IsDisposed) return;
                    Plan = p; result.Text = context + "\r\n\r\nAI 제안:\r\n" + p.explanation;
                    if (p.action == "rename") result.AppendText("\r\n대상 " + p.handle + ": " + p.old_tag + " → " + p.new_tag);
                    apply.Enabled = p.action == "rename";
                } catch (System.Exception ex) { if (!IsDisposed) result.Text = context + "\r\n\r\n" + ex.Message; }
                finally { if (!IsDisposed) { ask.Enabled = login.Enabled = logout.Enabled = model.Enabled = true; request.ReadOnly = false; } }
            };
        }
    }
}
