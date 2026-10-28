package com.illusion.messenger;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.media.AudioAttributes;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Base64;
import android.view.Window;
import android.view.WindowInsets;
import android.view.inputmethod.InputMethodManager;
import android.webkit.CookieManager;
import android.webkit.SafeBrowsingResponse;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.webkit.JavascriptInterface;
import android.webkit.SslErrorHandler;
import android.net.http.SslError;
import android.widget.FrameLayout;
import android.widget.Toast;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;

public final class MainActivity extends Activity {
    private static final int FILE_PICKER_REQUEST = 41;
    private static final int NOTIFICATION_PERMISSION_REQUEST = 42;
    private static final String MESSAGE_CHANNEL = "illusion_messages";
    private WebView webView;
    private ValueCallback<Uri[]> pendingFileCallback;
    private String pendingShare = "";
    private String pendingChat = "";
    private boolean pageReady = false;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        Window window = getWindow();
        window.setStatusBarColor(Color.rgb(9, 12, 19));
        window.setNavigationBarColor(Color.rgb(9, 12, 19));
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            window.setNavigationBarContrastEnforced(false);
        }

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.rgb(9, 12, 19));
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            } else {
                view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                        insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            }
            return insets;
        });

        webView = new WebView(this);
        webView.setBackgroundColor(Color.rgb(9, 12, 19));
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(true);
        settings.setAllowFileAccessFromFileURLs(false);
        settings.setAllowUniversalAccessFromFileURLs(false);
        settings.setSupportMultipleWindows(false);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setMediaPlaybackRequiresUserGesture(true);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) settings.setSafeBrowsingEnabled(true);

        CookieManager cookies = CookieManager.getInstance();
        cookies.setAcceptCookie(true);
        cookies.setAcceptThirdPartyCookies(webView, false);

        webView.setWebViewClient(new IllusionWebViewClient());
        webView.addJavascriptInterface(new IllusionBridge(), "IllusionBridge");
        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback,
                                             FileChooserParams params) {
                if (pendingFileCallback != null) pendingFileCallback.onReceiveValue(null);
                pendingFileCallback = callback;
                try {
                    Intent picker = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                    picker.addCategory(Intent.CATEGORY_OPENABLE);
                    String[] accepted = params.getAcceptTypes();
                    String primaryType = "*/*";
                    if (accepted != null) {
                        for (String type : accepted) {
                            if (type != null && type.contains("/")) {
                                primaryType = type;
                                break;
                            }
                        }
                    }
                    picker.setType(primaryType);
                    if (accepted != null && accepted.length > 1) picker.putExtra(Intent.EXTRA_MIME_TYPES, accepted);
                    picker.putExtra(Intent.EXTRA_ALLOW_MULTIPLE,
                            params.getMode() == FileChooserParams.MODE_OPEN_MULTIPLE);
                    picker.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivityForResult(picker, FILE_PICKER_REQUEST);
                    return true;
                } catch (ActivityNotFoundException | SecurityException error) {
                    pendingFileCallback = null;
                    callback.onReceiveValue(null);
                    Toast.makeText(MainActivity.this, "No compatible file picker is available.", Toast.LENGTH_SHORT).show();
                    return true;
                }
            }

            @Override
            public void onPermissionRequest(android.webkit.PermissionRequest request) {
                boolean audio=false;
                for (String resource : request.getResources()) {
                    if ("android.webkit.resource.AUDIO_CAPTURE".equals(resource)) audio=true;
                }
                if (audio && checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                    request.grant(new String[]{"android.webkit.resource.AUDIO_CAPTURE"});
                } else {
                    request.deny();
                }
            }
        });

        root.addView(webView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
        setContentView(root);
        if (checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != android.content.pm.PackageManager.PERMISSION_GRANTED) requestPermissions(new String[]{android.Manifest.permission.RECORD_AUDIO}, 4102);
        ensureNotificationChannel();
        handleIncomingIntent(getIntent());

        if (state == null || webView.restoreState(state) == null) webView.loadUrl(BuildConfig.ILLUSION_START_URL);
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handleIncomingIntent(intent);
    }

    private void handleIncomingIntent(Intent intent) {
        if (intent == null) return;
        String chat = intent.getStringExtra("illusion_chat");
        if (chat != null && !chat.isEmpty()) { pendingChat = chat; deliverPendingChat(); }
        if (!Intent.ACTION_SEND.equals(intent.getAction())) return;
        String text = intent.getStringExtra(Intent.EXTRA_TEXT);
        Uri stream = intent.getParcelableExtra(Intent.EXTRA_STREAM);
        String type = intent.getType();
        final String sharedText = text == null ? "" : text;
        if (type != null && type.startsWith("image/") && stream != null) {
            new Thread(() -> {
                String image = shareImage(stream, type);
                if (!image.isEmpty() || !sharedText.trim().isEmpty()) deliverShare(sharedText, image);
            }).start();
        } else if (!sharedText.trim().isEmpty()) {
            deliverShare(sharedText, "");
        }
    }

    private void deliverShare(String text, String image) {
        try {
            JSONObject share = new JSONObject();
            share.put("text", text == null ? "" : text);
            share.put("image", image == null ? "" : image);
            share.put("name", "shared-image.jpg");
            pendingShare = share.toString();
            runOnUiThread(this::deliverPendingShare);
        } catch (Exception ignored) {}
    }

    private void deliverPendingShare() {
        if (!pageReady || pendingShare.isEmpty() || webView == null) return;
        final String payload = pendingShare;
        pendingShare = "";
        webView.evaluateJavascript("window.illusionReceiveShare&&window.illusionReceiveShare(" + JSONObject.quote(payload) + ")", null);
    }

    private void deliverPendingChat() {
        if (!pageReady || pendingChat.isEmpty() || webView == null) return;
        final String username = pendingChat;
        pendingChat = "";
        webView.evaluateJavascript("window.illusionOpenChat&&window.illusionOpenChat(" + JSONObject.quote(username) + ")", null);
    }

    private String shareImage(Uri uri, String fallbackType) {
        try {
            String mime = getContentResolver().getType(uri);
            if (mime == null) mime = fallbackType;
            if (mime == null || !mime.startsWith("image/")) return "";
            byte[] input = readLimited(uri, 4 * 1024 * 1024);
            if (input == null) return "";
            BitmapFactory.Options bounds = new BitmapFactory.Options();
            bounds.inJustDecodeBounds = true;
            BitmapFactory.decodeByteArray(input, 0, input.length, bounds);
            if (bounds.outWidth <= 0 || bounds.outHeight <= 0 || bounds.outWidth > 8192 || bounds.outHeight > 8192) return "";
            if ((long) bounds.outWidth * bounds.outHeight > 16000000L) return "";
            int sample = 1;
            while (bounds.outWidth / sample > 1600 || bounds.outHeight / sample > 1600) sample *= 2;
            BitmapFactory.Options options = new BitmapFactory.Options();
            options.inSampleSize = sample;
            Bitmap decoded = BitmapFactory.decodeByteArray(input, 0, input.length, options);
            if (decoded == null) return "";
            double scale = Math.min(1.0, 1600.0 / Math.max(decoded.getWidth(), decoded.getHeight()));
            Bitmap scaled = decoded;
            if (scale < 1) {
                scaled = Bitmap.createScaledBitmap(decoded, Math.max(1, (int) Math.round(decoded.getWidth() * scale)), Math.max(1, (int) Math.round(decoded.getHeight() * scale)), true);
                if (scaled != decoded) decoded.recycle();
            }
            byte[] jpeg = null;
            for (int quality : new int[]{85, 70, 55, 40}) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                scaled.compress(Bitmap.CompressFormat.JPEG, quality, out);
                jpeg = out.toByteArray();
                if (jpeg.length <= 4 * 1024 * 1024) break;
            }
            scaled.recycle();
            if (jpeg == null || jpeg.length > 4 * 1024 * 1024) return "";
            return "data:image/jpeg;base64," + Base64.encodeToString(jpeg, Base64.NO_WRAP);
        } catch (Exception ignored) {
            return "";
        }
    }

    private byte[] readLimited(Uri uri, int limit) {
        try (InputStream in = getContentResolver().openInputStream(uri); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            if (in == null) return null;
            byte[] buffer = new byte[8192];
            int read, total = 0;
            while ((read = in.read(buffer)) != -1) {
                total += read;
                if (total > limit) return null;
                out.write(buffer, 0, read);
            }
            return out.toByteArray();
        } catch (Exception ignored) {
            return null;
        }
    }

    private void ensureNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return;
        Uri sound = Uri.parse("android.resource://" + getPackageName() + "/" + R.raw.illusionsfx);
        AudioAttributes audio = new AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_NOTIFICATION)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build();
        NotificationChannel channel = new NotificationChannel(MESSAGE_CHANNEL, "New messages", NotificationManager.IMPORTANCE_HIGH);
        channel.setDescription("Illusion message alerts");
        channel.setSound(sound, audio);
        getSystemService(NotificationManager.class).createNotificationChannel(channel);
    }

    private final class IllusionBridge {
        @JavascriptInterface
        public void requestNotifications() {
            runOnUiThread(() -> {
                if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                    requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS}, NOTIFICATION_PERMISSION_REQUEST);
                } else {
                    android.widget.Toast.makeText(MainActivity.this, "Illusion notifications are enabled.", Toast.LENGTH_SHORT).show();
                }
            });
        }

        @JavascriptInterface
        public void notifyMessage(String title, String body) {
            notifyMessage(title, body, "");
        }

        @JavascriptInterface
        public void notifyMessage(String title, String body, String chat) {
            runOnUiThread(() -> {
                if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) return;
                NotificationManager manager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
                Intent open = new Intent(MainActivity.this, MainActivity.class);
                open.setFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
                open.putExtra("illusion_chat", chat == null ? "" : chat);
                PendingIntent pending = PendingIntent.getActivity(MainActivity.this, 0, open,
                        PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0));
                Notification.Builder notice = Build.VERSION.SDK_INT >= 26
                        ? new Notification.Builder(MainActivity.this, MESSAGE_CHANNEL)
                        : new Notification.Builder(MainActivity.this).setSound(Uri.parse("android.resource://" + getPackageName() + "/" + R.raw.illusionsfx));
                notice.setSmallIcon(R.drawable.ic_illusion)
                        .setContentTitle(safeNotificationText(title, "Illusion"))
                        .setContentText(safeNotificationText(body, "New message"))
                        .setAutoCancel(true).setContentIntent(pending);
                manager.notify((int) (System.currentTimeMillis() & 0x7fffffff), notice.build());
            });
        }

        @JavascriptInterface
        public void focusKeyboard() {
            runOnUiThread(() -> {
                if (webView == null) return;
                webView.requestFocus();
                InputMethodManager input = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
                if (input != null) input.showSoftInput(webView, InputMethodManager.SHOW_IMPLICIT);
            });
        }

        @JavascriptInterface
        public String takePendingShare() {
            String value = pendingShare;
            pendingShare = "";
            return value;
        }

        @JavascriptInterface
        public String takePendingChat() {
            String value = pendingChat;
            pendingChat = "";
            return value;
        }

        private String safeNotificationText(String value, String fallback) {
            if (value == null) return fallback;
            String clean = value.replaceAll("[\\p{Cntrl}]", " ").trim();
            return clean.isEmpty() ? fallback : clean.substring(0, Math.min(80, clean.length()));
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != FILE_PICKER_REQUEST || pendingFileCallback == null) return;
        Uri[] selected = WebChromeClient.FileChooserParams.parseResult(resultCode, data);
        pendingFileCallback.onReceiveValue(selected);
        pendingFileCallback = null;
    }

    @Override
    protected void onSaveInstanceState(Bundle state) {
        if (webView != null) webView.saveState(state);
        super.onSaveInstanceState(state);
    }

    @Override
    public void onBackPressed() {
        if (webView != null && webView.canGoBack()) webView.goBack();
        else super.onBackPressed();
    }

    @Override
    protected void onDestroy() {
        if (pendingFileCallback != null) pendingFileCallback.onReceiveValue(null);
        if (webView != null) {
            webView.stopLoading();
            webView.setWebChromeClient(null);
            webView.setWebViewClient(null);
            webView.destroy();
            webView = null;
        }
        super.onDestroy();
    }

    private final class IllusionWebViewClient extends WebViewClient {
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return routeUrl(request.getUrl());
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, String url) {
            return routeUrl(Uri.parse(url));
        }

        private boolean routeUrl(Uri uri) {
            String scheme = uri.getScheme() == null ? "" : uri.getScheme().toLowerCase();
            String host = uri.getHost();
            Uri origin = Uri.parse(BuildConfig.ILLUSION_ORIGIN);
            if ("https".equals(scheme) && host != null && host.equalsIgnoreCase(origin.getHost())
                    && uri.getUserInfo() == null && (uri.getPort() == -1 || uri.getPort() == 443)) {
                return false;
            }
            if (!"https".equals(scheme) && !"mailto".equals(scheme) && !"tel".equals(scheme)) return true;
            try {
                Intent external = new Intent("mailto".equals(scheme) ? Intent.ACTION_SENDTO : Intent.ACTION_VIEW, uri);
                startActivity(external);
            } catch (ActivityNotFoundException ignored) {
                Toast.makeText(MainActivity.this, "No app is available to open that link.", Toast.LENGTH_SHORT).show();
            }
            return true;
        }

        @Override
        public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
            handler.cancel();
        }

        @Override
        public void onSafeBrowsingHit(WebView view, WebResourceRequest request, int threatType,
                                      SafeBrowsingResponse response) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) response.backToSafety(true);
            else view.stopLoading();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            pageReady = true;
            deliverPendingShare();
            deliverPendingChat();
        }
    }
}
