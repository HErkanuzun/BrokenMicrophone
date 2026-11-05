package io.github.herkanuzun.brokenmicrophone;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Handler;
import android.os.Looper;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import javax.net.ssl.HttpsURLConnection;

/**
 * Güncelleme denetimi: GitHub'daki son sürümün numarasını okur (api.github.com, sadece sürüm numarası; hiçbir bilgi
 * gönderilmez). Günde en fazla iki kez, Ayarlar'dan kapatılabilir. İndirme her zaman GitHub'dan, aynı imzayla.
 */
final class Updates {
    static final String API = "https://api.github.com/repos/HErkanuzun/BrokenMicrophone/releases/latest";
    static final String APK = "https://github.com/HErkanuzun/BrokenMicrophone/releases/latest/download/BrokenMicrophone.apk";
    static final String PAGE = "https://herkanuzun.github.io/BrokenMicrophone/";
    static final long EVERY = 12L * 60 * 60 * 1000;

    interface Done { void done(String latest, boolean ok); }

    private static final Pattern TAG = Pattern.compile("\"tag_name\"\\s*:\\s*\"v?([0-9]+(?:\\.[0-9]+){0,3})\"");

    static boolean enabled(SharedPreferences p) { return p.getBoolean("updates", true); }

    /** Kaydedilmiş en yeni sürüm (denetlenmediyse ""). */
    static String latest(SharedPreferences p) { return p.getString("latest", ""); }

    static boolean newer(String latest) { return latest != null && !latest.isEmpty() && compare(latest, BuildInfo.VERSION) > 0; }

    /** force=false ise son denetimden 12 saat geçmediyse bir şey yapmaz. Sonuç ana iş parçacığında gelir. */
    static void check(Context c, boolean force, Done cb) {
        final SharedPreferences p = c.getSharedPreferences("settings", Context.MODE_PRIVATE);
        long now = System.currentTimeMillis();
        if (!force && (!enabled(p) || now - p.getLong("checkedAt", 0) < EVERY)) return;
        p.edit().putLong("checkedAt", now).apply();
        final Handler main = new Handler(Looper.getMainLooper());
        new Thread(() -> {
            String v = fetch();
            if (v != null) p.edit().putString("latest", v).apply();
            final String r = v;
            main.post(() -> { if (cb != null) cb.done(r != null ? r : latest(p), r != null); });
        }, "update-check").start();
    }

    private static String fetch() {
        HttpsURLConnection h = null;
        try {
            h = (HttpsURLConnection) new URL(API).openConnection();
            h.setConnectTimeout(8000);
            h.setReadTimeout(8000);
            h.setInstanceFollowRedirects(true);
            h.setRequestProperty("Accept", "application/vnd.github+json");
            h.setRequestProperty("User-Agent", "BrokenMicrophone-Android/" + BuildInfo.VERSION);
            if (h.getResponseCode() != 200) return null;
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            try (InputStream in = h.getInputStream()) {
                byte[] b = new byte[8192];
                int n;
                while ((n = in.read(b)) > 0 && out.size() < 512 * 1024) out.write(b, 0, n);
            }
            Matcher m = TAG.matcher(new String(out.toByteArray(), StandardCharsets.UTF_8));
            return m.find() ? m.group(1) : null;
        } catch (Exception e) {
            return null;
        } finally {
            if (h != null) h.disconnect();
        }
    }

    /** "1.10" > "1.9"; eksik kısımlar 0. */
    static int compare(String a, String b) {
        String[] x = a.split("\\."), y = b.split("\\.");
        for (int i = 0; i < Math.max(x.length, y.length); i++) {
            int p = i < x.length ? num(x[i]) : 0, q = i < y.length ? num(y[i]) : 0;
            if (p != q) return p < q ? -1 : 1;
        }
        return 0;
    }

    private static int num(String s) {
        try { return Integer.parseInt(s.replaceAll("[^0-9]", "")); } catch (NumberFormatException e) { return 0; }
    }

    private Updates() { }
}
