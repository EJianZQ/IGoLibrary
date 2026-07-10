using System.Text.Json;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class LanCookieRelayMobilePage
{
  public static string Build(
    string token,
    LanAuthLinkRelayPurpose purpose = LanAuthLinkRelayPurpose.GraphQlSession)
  {
    var tokenJson = JsonSerializer.Serialize(token);
    var purposeTitle = purpose == LanAuthLinkRelayPurpose.RemoteCheckIn
      ? "远程签到授权局域网快传"
      : "登录授权局域网快传";
    var purposeHint = purpose == LanAuthLinkRelayPurpose.RemoteCheckIn
      ? "请重新扫码获取一个未被普通登录使用的新授权链接，提交后仅用于远程签到"
      : "提交后用于获取普通登录 Cookie";
    return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="Cache-Control" content="no-store">
  <title>{{purposeTitle}}</title>
  <style>
    :root { color-scheme: light; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
    body { margin: 0; background: #f5f7fb; color: #1d2129; }
    main { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px; box-sizing: border-box; }
    section { width: min(100%, 440px); background: #fff; border: 1px solid #e7eaee; border-radius: 18px; padding: 22px; box-shadow: 0 10px 28px rgba(15,23,42,.08); }
    h1 { margin: 0 0 8px; font-size: 22px; line-height: 1.3; }
    p { margin: 0 0 18px; color: #4e5969; font-size: 14px; line-height: 1.6; }
    .qr-card { margin: 16px 0; padding: 14px; border-radius: 14px; background: #f7f9fc; border: 1px solid #edf1f7; text-align: center; }
    .qr-title { margin: 0 0 10px; color: #1d2129; font-size: 15px; font-weight: 700; }
    .qr-frame { width: min(100%, 232px); aspect-ratio: 1; margin: 0 auto 10px; padding: 10px; border-radius: 12px; background: #fff; box-shadow: 0 1px 4px rgba(15,23,42,.06); }
    .qr-frame img { display: block; width: 100%; height: 100%; object-fit: contain; image-rendering: pixelated; }
    .qr-hint { margin: 0; color: #6b7785; font-size: 13px; }
    button { width: 100%; min-height: 46px; border: 0; border-radius: 12px; font-size: 16px; font-weight: 700; }
    button.primary { background: #0077fa; color: #fff; }
    button.secondary { margin-bottom: 12px; background: #e8f3ff; color: #0066d6; }
    textarea { width: 100%; min-height: 136px; margin: 0 0 14px; padding: 12px; box-sizing: border-box; border: 1px solid #d8dee8; border-radius: 12px; font-size: 15px; line-height: 1.45; resize: vertical; }
    #message { min-height: 22px; margin-top: 14px; font-size: 14px; line-height: 1.5; color: #4e5969; }
    .ok { color: #14804a; }
    .bad { color: #c2410c; }
  </style>
</head>
<body>
  <main>
    <section>
      <h1>{{purposeTitle}}</h1>
      <p>在微信内长按识别下方授权二维码，完成授权后复制包含 code 的链接，返回本页提交到电脑端。{{purposeHint}}</p>
      <div class="qr-card">
        <div class="qr-title">微信授权二维码</div>
        <div class="qr-frame">
          <img id="authQrCode" alt="微信授权二维码">
        </div>
        <p class="qr-hint">如果长按不可用，可以截图后在微信扫一扫中识别</p>
      </div>
      <button class="secondary" id="paste" type="button">点击粘贴链接</button>
      <textarea id="link" autocomplete="off" autocapitalize="off" spellcheck="false" placeholder="在这里粘贴包含 code 的授权链接"></textarea>
      <button class="primary" id="send" type="button">发送到电脑</button>
      <div id="message" aria-live="polite"></div>
    </section>
  </main>
  <script>
    const token = {{tokenJson}};
    const link = document.getElementById('link');
    const message = document.getElementById('message');
    const send = document.getElementById('send');
    document.getElementById('authQrCode').src = '/auth-qrcode?token=' + encodeURIComponent(token);
    const setMessage = (text, kind) => {
      message.textContent = text;
      message.className = kind || '';
    };
    document.getElementById('paste').addEventListener('click', async () => {
      try {
        if (!navigator.clipboard || !navigator.clipboard.readText) {
          setMessage('当前浏览器不支持直接读取剪贴板，请长按输入框手动粘贴', 'bad');
          return;
        }
        link.value = await navigator.clipboard.readText();
        setMessage('已读取剪贴板，可以发送', 'ok');
      } catch {
        setMessage('微信浏览器可能限制 HTTP 页面读取剪贴板，请长按输入框手动粘贴', 'bad');
      }
    });
    send.addEventListener('click', async () => {
      const value = link.value.trim();
      if (!value) {
        setMessage('请先粘贴授权链接', 'bad');
        return;
      }
      send.disabled = true;
      setMessage('正在发送到电脑...', '');
      try {
        const response = await fetch('/submit?token=' + encodeURIComponent(token), {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ link: value })
        });
        const result = await response.json();
        setMessage(result.message || (result.success ? '已发送成功' : '发送失败'), result.success ? 'ok' : 'bad');
      } catch {
        setMessage('发送失败，请确认手机和电脑在同一局域网', 'bad');
      } finally {
        send.disabled = false;
      }
    });
  </script>
</body>
</html>
""";
  }
}
