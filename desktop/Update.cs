using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Illusion {
  public partial class Messenger {
    const long UpdateMaxBytes = 50000000;

    internal static int[] ParseVersion(string value) {
      string[] parts = (value ?? "").Split('.');
      if (parts.Length != 3) throw new FormatException("version");
      var numbers = new int[3];
      for (int i = 0; i < 3; i++) if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) throw new FormatException("version");
      return numbers;
    }

    internal static int CompareVersions(string left, string right) {
      int[] a = ParseVersion(left), b = ParseVersion(right);
      for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
      return 0;
    }

    internal static string Sha256(byte[] bytes) {
      using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    internal static Dictionary<string, object> ValidateUpdateManifest(Dictionary<string, object> manifest) {
      if (manifest == null) throw new Exception("Invalid update manifest.");
      string version = manifest.ContainsKey("version") ? Convert.ToString(manifest["version"]) : "";
      ParseVersion(version);
      string file = manifest.ContainsKey("file") ? Convert.ToString(manifest["file"]) : "";
      if (file != "Illusion-" + version + ".exe") throw new Exception("Invalid update file name.");
      long bytes = manifest.ContainsKey("bytes") ? Convert.ToInt64(manifest["bytes"]) : 0;
      if (bytes <= 0 || bytes > UpdateMaxBytes) throw new Exception("Invalid update size.");
      string sha = (manifest.ContainsKey("sha256") ? Convert.ToString(manifest["sha256"]) : "").ToLowerInvariant();
      if (sha.Length != 64) throw new Exception("Invalid update hash.");
      for (int i = 0; i < sha.Length; i++) if ("0123456789abcdef".IndexOf(sha[i]) < 0) throw new Exception("Invalid update hash.");
      return new Dictionary<string, object> { { "version", version }, { "file", file }, { "bytes", bytes }, { "sha256", sha } };
    }

    internal static void VerifyUpdateBytes(byte[] bytes, long expectedBytes, string expectedSha) {
      if (bytes == null || bytes.LongLength != expectedBytes) throw new Exception("The update size does not match the manifest.");
      if (Sha256(bytes) != expectedSha.ToLowerInvariant()) throw new Exception("The update hash does not match the manifest.");
    }

    internal static bool IsSecureUpdateOrigin(string address) {
      Uri uri;
      return Uri.TryCreate(address, UriKind.Absolute, out uri) && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    void CheckForUpdates() {
      if (string.IsNullOrEmpty(server) || !IsSecureUpdateOrigin(server)) return;
      long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), last = 0;
      long.TryParse(Setting("updateCheckedAt", "0"), out last);
      if (now - last < 24 * 3600 * 1000) return;
      settings["updateCheckedAt"] = now.ToString();
      SaveSettings();
      Task.Run(async () => {
        try {
          using (var response = await http.GetAsync(server + "/download/release.json")) {
            if (!response.IsSuccessStatusCode) return;
            string text = await response.Content.ReadAsStringAsync();
            if (text.Length > 100000) return;
            var manifest = ValidateUpdateManifest(json.Deserialize<Dictionary<string, object>>(text));
            if (CompareVersions((string)manifest["version"], BuildInfo.Version) <= 0) return;
            Window.Dispatcher.BeginInvoke((Action)(() => ShowUpdateDialog(manifest)));
          }
        } catch { }
      });
    }

    void ShowUpdateDialog(Dictionary<string, object> manifest) {
      var panel = new StackPanel();
      panel.Children.Add(Label(T("A new version of Illusion is ready to install.")));
      panel.Children.Add(Label("v" + manifest["version"] + " · " + BuildInfo.Version, 11));
      var status = Label(""); panel.Children.Add(status);
      var install = new Button { Content = T("Install now"), Background = (Brush)Window.Resources["Accent"] };
      var later = new Button { Content = T("Later"), Margin = new Thickness(0, 8, 0, 0) };
      panel.Children.Add(install); panel.Children.Add(later);
      var dialog = Dialog(T("Update available"), panel);
      later.Click += (s, e) => dialog.Close();
      install.Click += async (s, e) => {
        install.IsEnabled = false; later.IsEnabled = false; status.Text = T("Downloading update…");
        try {
          byte[] bytes;
          using (var response = await http.GetAsync(server + "/download/" + (string)manifest["file"])) {
            if (!response.IsSuccessStatusCode) throw new Exception();
            bytes = await response.Content.ReadAsByteArrayAsync();
          }
          VerifyUpdateBytes(bytes, (long)manifest["bytes"], (string)manifest["sha256"]);
          string target = Path.Combine(Path.GetTempPath(), (string)manifest["file"]);
          File.WriteAllBytes(target, bytes);
          LaunchUpdater(target);
          dialog.Close();
          Application.Current.Shutdown();
        } catch {
          status.Text = T("The update could not be installed. Your current version is unchanged.");
          install.IsEnabled = true; later.IsEnabled = true;
        }
      };
      dialog.ShowDialog();
    }

    void LaunchUpdater(string newExe) {
      string current = Process.GetCurrentProcess().MainModule.FileName;
      string backup = Path.Combine(Path.GetDirectoryName(current), "Illusion-previous.exe");
      string script = Path.Combine(Path.GetTempPath(), "illusion-update-" + Guid.NewGuid().ToString("N") + ".ps1");
      Func<string, string> quote = value => "'" + value.Replace("'", "''") + "'";
      string body =
        "$p=" + Process.GetCurrentProcess().Id + "\n" +
        "Wait-Process -Id $p -ErrorAction SilentlyContinue\n" +
        "$current=" + quote(current) + "\n$new=" + quote(newExe) + "\n$backup=" + quote(backup) + "\n" +
        "for($i=0;$i -lt 120;$i++){\n" +
        "  try {\n" +
        "    if(Test-Path $current){ if(Test-Path $backup){ Remove-Item $backup -Force -ErrorAction SilentlyContinue }; Move-Item $current $backup -Force }\n" +
        "    Move-Item $new $current -Force\n" +
        "    Start-Process $current\n" +
        "    break\n" +
        "  } catch {\n" +
        "    if((!(Test-Path $current)) -and (Test-Path $backup)){ Move-Item $backup $current -Force -ErrorAction SilentlyContinue }\n" +
        "    Start-Sleep -Milliseconds 500\n" +
        "  }\n" +
        "}\n" +
        "Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue\n";
      File.WriteAllText(script, body, new UTF8Encoding(false));
      var start = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true };
      if (Process.Start(start) == null) throw new Exception("Updater did not start.");
    }
  }
}
