package io.github.herkanuzun.brokenmicrophone;

import android.content.Context;
import android.content.SharedPreferences;

import java.security.MessageDigest;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;

import javax.net.ssl.X509TrustManager;

/**
 * Bilgisayarın kendinden imzalı sertifikası. İlk bağlantıda parmak izi kaydedilir,
 * sonraki bağlantılarda aynı sertifika beklenir (araya giren başka bir cihaz kabul edilmez).
 */
final class Pins implements X509TrustManager {
    private final SharedPreferences prefs;
    private final String id;
    volatile boolean mismatch;

    Pins(Context c, String id) {
        prefs = c.getSharedPreferences("pins", Context.MODE_PRIVATE);
        this.id = id;
    }

    static void forget(Context c, String id) {
        c.getSharedPreferences("pins", Context.MODE_PRIVATE).edit().remove(id).apply();
    }

    @Override
    public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
        if (chain == null || chain.length == 0) throw new CertificateException("no certificate");
        String fp = sha256(chain[0].getEncoded());
        String saved = prefs.getString(id, null);
        if (saved == null) {
            prefs.edit().putString(id, fp).apply();
            return;
        }
        if (!saved.equals(fp)) {
            mismatch = true;
            throw new CertificateException("certificate changed");
        }
    }

    @Override
    public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
        throw new CertificateException("not a server");
    }

    @Override
    public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }

    static String sha256(byte[] data) throws CertificateException {
        try {
            byte[] h = MessageDigest.getInstance("SHA-256").digest(data);
            StringBuilder b = new StringBuilder();
            for (byte x : h) b.append(String.format("%02x", x & 0xff));
            return b.toString();
        } catch (Exception e) {
            throw new CertificateException(e);
        }
    }
}
