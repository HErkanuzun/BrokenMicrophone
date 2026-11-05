package io.github.herkanuzun.brokenmicrophone;

import java.util.Locale;

/** Türkçe telefonda Türkçe, diğerlerinde İngilizce. */
final class L {
    static final boolean TR = "tr".equals(Locale.getDefault().getLanguage());

    static String t(String tr, String en) { return TR ? tr : en; }

    private L() { }
}
