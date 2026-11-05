package io.github.herkanuzun.brokenmicrophone;

import android.content.Context;

import java.util.Locale;

/** Uygulama dili: ilk açılışta kullanıcı seçer (Ayarlar'dan değişir). Seçilmediyse telefonun dili. */
final class L {
    static boolean TR = "tr".equals(Locale.getDefault().getLanguage());

    static void init(Context c) {
        String l = c.getSharedPreferences("settings", Context.MODE_PRIVATE).getString("lang", "");
        if (!l.isEmpty()) TR = "tr".equals(l);
    }

    static boolean chosen(Context c) {
        return !c.getSharedPreferences("settings", Context.MODE_PRIVATE).getString("lang", "").isEmpty();
    }

    static void set(Context c, boolean tr) {
        TR = tr;
        c.getSharedPreferences("settings", Context.MODE_PRIVATE).edit().putString("lang", tr ? "tr" : "en").apply();
    }

    static String t(String tr, String en) { return TR ? tr : en; }

    private L() { }
}
