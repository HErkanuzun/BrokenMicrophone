package io.github.herkanuzun.brokenmicrophone;

import android.os.Handler;
import android.os.Looper;

import java.util.concurrent.CopyOnWriteArrayList;

/** Servis ile ekran arasındaki ortak durum. Servis yazar, ekran okur. */
final class Hub {
    static final int IDLE = 0, CONNECTING = 1, LIVE = 2, ERROR = 3;

    static volatile int state = IDLE;
    /** ERROR ya da CONNECTING sırasında neden: "badKey", "cert", "mic", "unreachable" ya da "". */
    static volatile String reason = "";
    static volatile String target = "";
    static volatile String mic = "";
    static volatile boolean running;
    static volatile boolean muted;
    static volatile float peak;

    interface Listener { void changed(); }

    private static final CopyOnWriteArrayList<Listener> listeners = new CopyOnWriteArrayList<>();
    private static final Handler main = new Handler(Looper.getMainLooper());

    static void add(Listener l) { listeners.add(l); }
    static void remove(Listener l) { listeners.remove(l); }

    static void set(int s, String why) {
        state = s;
        reason = why == null ? "" : why;
        fire();
    }

    static void fire() {
        main.post(() -> { for (Listener l : listeners) l.changed(); });
    }

    private Hub() { }
}
