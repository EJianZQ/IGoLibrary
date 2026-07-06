using System.Text.Json;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class LanCookieRelayMobilePage
{
  public static string Build(string token)
  {
    var tokenJson = JsonSerializer.Serialize(token);
    return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta http-equiv="Cache-Control" content="no-store">
  <title>发送授权链接到电脑</title>
  <style>
    :root { color-scheme: light; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
    body { margin: 0; background: #f5f7fb; color: #1d2129; }
    main { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px; box-sizing: border-box; }
    section { width: min(100%, 440px); background: #fff; border: 1px solid #e7eaee; border-radius: 18px; padding: 22px; box-shadow: 0 10px 28px rgba(15,23,42,.08); }
    h1 { margin: 0 0 8px; font-size: 22px; line-height: 1.3; }
    p { margin: 0 0 18px; color: #4e5969; font-size: 14px; line-height: 1.6; }
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
      <h1>发送授权链接到电脑</h1>
      <p>在微信里复制“我去图书馆”授权链接后，点击粘贴或手动粘贴，再发送到电脑端</p>
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
