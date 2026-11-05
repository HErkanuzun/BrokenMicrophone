package io.github.herkanuzun.brokenmicrophone;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Bağlantı anahtarını telefonda şifreli saklar. Şifreleme anahtarı Android Keystore'da durur
 * (destekleyen telefonlarda donanımda); uygulama dışına çıkarılamaz, yedeklere girmez.
 */
final class Secret {
    private static final String ALIAS = "brokenmic-settings";

    private static SecretKey key() throws Exception {
        KeyStore ks = KeyStore.getInstance("AndroidKeyStore");
        ks.load(null);
        KeyStore.Entry e = ks.getEntry(ALIAS, null);
        if (e instanceof KeyStore.SecretKeyEntry) return ((KeyStore.SecretKeyEntry) e).getSecretKey();
        KeyGenerator g = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        g.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return g.generateKey();
    }

    static String seal(String plain) {
        if (plain == null || plain.isEmpty()) return "";
        try {
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.ENCRYPT_MODE, key());
            byte[] iv = c.getIV();
            byte[] ct = c.doFinal(plain.getBytes(StandardCharsets.UTF_8));
            byte[] out = new byte[1 + iv.length + ct.length];
            out[0] = (byte) iv.length;
            System.arraycopy(iv, 0, out, 1, iv.length);
            System.arraycopy(ct, 0, out, 1 + iv.length, ct.length);
            return "v1:" + Base64.encodeToString(out, Base64.NO_WRAP);
        } catch (Exception e) {
            return "";     // saklanamazsa hiç saklanmaz; kullanıcı QR'ı yeniden okutur
        }
    }

    static String open(String sealed) {
        if (sealed == null || !sealed.startsWith("v1:")) return "";
        try {
            byte[] in = Base64.decode(sealed.substring(3), Base64.NO_WRAP);
            int n = in[0];
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, in, 1, n));
            return new String(c.doFinal(in, 1 + n, in.length - 1 - n), StandardCharsets.UTF_8);
        } catch (Exception e) {
            return "";
        }
    }

    private Secret() { }
}
