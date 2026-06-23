namespace EduGuardAgent.Services;

using EduGuardAgent.Profiles;

internal static class BlockPageHtml
{
    public static string Build(string blockedHost, string? modeDisplayName = null)
    {
        var safeHost = System.Net.WebUtility.HtmlEncode(blockedHost);
        var modeLabel = modeDisplayName ?? AgentModeRegistry.Sub.DisplayName;
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Unsafe place — EduGuard</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      font-family: "Segoe UI", system-ui, sans-serif;
      background: linear-gradient(145deg, #7b57c7 0%, #5a3e9c 55%, #4e3380 100%);
      color: #2d2640;
      padding: 24px;
    }
    .card {
      width: min(520px, 100%);
      background: #fff;
      border-radius: 24px;
      padding: 40px 36px;
      text-align: center;
      box-shadow: 0 24px 60px rgba(45, 38, 64, 0.25);
    }
    .shield {
      width: 88px;
      height: 88px;
      margin: 0 auto 20px;
      border-radius: 22px;
      background: #ede7ff;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 44px;
    }
    .mascot-note {
      font-size: 12px;
      color: #9a92ad;
      margin-bottom: 8px;
    }
    h1 {
      font-size: 30px;
      margin-bottom: 10px;
      color: #4e3380;
    }
    .host {
      display: inline-block;
      background: #f7f4ff;
      color: #6d4cb3;
      font-weight: 700;
      padding: 8px 16px;
      border-radius: 10px;
      margin: 12px 0 18px;
      word-break: break-all;
    }
    p {
      color: #7a708c;
      line-height: 1.55;
      font-size: 16px;
    }
    .level {
      margin-top: 24px;
      padding-top: 20px;
      border-top: 1px solid #e8e1f8;
      color: #6d4cb3;
      font-weight: 600;
      font-size: 14px;
    }
    .note {
      margin-top: 14px;
      font-size: 13px;
      color: #9a92ad;
    }
  </style>
</head>
<body>
  <div class="card">
    <p class="mascot-note">Guardi blocked this for your safety</p>
    <div class="shield">🛡️</div>
    <h1>This place isn't safe for you</h1>
    <div class="host">__HOST__</div>
    <p>Your Dom put this website off-limits on your protected computer.</p>
    <p class="note">Stay in your Safety Zone — breaking rules can mean more infractions and stricter protections.</p>
    <div class="level">__LEVEL__</div>
  </div>
</body>
</html>
""".Replace("__HOST__", safeHost).Replace("__LEVEL__", modeLabel);
    }
}
