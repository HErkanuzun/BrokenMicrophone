package io.github.herkanuzun.brokenmicrophone;

import android.Manifest;
import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.media.AudioDeviceCallback;
import android.media.AudioDeviceInfo;
import android.media.AudioManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public class MainActivity extends Activity implements Hub.Listener {
    static final int DEFAULT_PORT = 47820;

    private SharedPreferences prefs;
    private AudioManager am;
    private final Handler ui = new Handler(Looper.getMainLooper());

    private Ui.Dot dot;
    private TextView statusTitle, statusDetail, micHint;
    private TextView muteBtn, trustBtn;
    private Ui.MicButton micBtn;
    private Ui.Meter meter;
    private EditText addrField, keyField;
    private Spinner micSpin;
    private final List<String> micKeys = new ArrayList<>();
    private boolean fillingSpinner;
    private boolean pendingStart;
    private boolean resumed;

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            if (!resumed) return;
            float lv = meter.push(Hub.running ? Hub.peak : 0);
            micBtn.set(micMode(), lv);
            ui.postDelayed(this, 33);
        }
    };

    private final AudioDeviceCallback deviceWatch = new AudioDeviceCallback() {
        @Override public void onAudioDevicesAdded(AudioDeviceInfo[] added) { fillMics(); }
        @Override public void onAudioDevicesRemoved(AudioDeviceInfo[] removed) { fillMics(); }
    };

    // ------------------------------------------------------------------ yaşam döngüsü
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        prefs = getSharedPreferences("settings", MODE_PRIVATE);
        am = (AudioManager) getSystemService(AUDIO_SERVICE);
        Window w = getWindow();
        w.setBackgroundDrawable(new Ui.Backdrop());
        w.setStatusBarColor(Color.TRANSPARENT);
        w.setNavigationBarColor(Color.TRANSPARENT);
        w.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
        setContentView(build());
        fillMics();
        handle(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handle(intent);
    }

    @Override
    protected void onResume() {
        super.onResume();
        resumed = true;
        Hub.add(this);
        am.registerAudioDeviceCallback(deviceWatch, ui);
        changed();
        ui.post(tick);
    }

    @Override
    protected void onPause() {
        resumed = false;
        Hub.remove(this);
        am.unregisterAudioDeviceCallback(deviceWatch);
        super.onPause();
    }

    // ------------------------------------------------------------------ ekran
    private int px(float dp) { return Math.round(Ui.dp(this, dp)); }

    private TextView text(float sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTextColor(color);
        if (bold) t.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        t.setLineSpacing(0, 1.15f);
        return t;
    }

    private LinearLayout card(LinearLayout parent) {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setBackground(Ui.card(this));
        c.setPadding(px(18), px(16), px(18), px(18));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.bottomMargin = px(14);
        parent.addView(c, lp);
        return c;
    }

    private TextView heading(LinearLayout parent, String s) {
        TextView t = text(13, SUB_HEAD, true);
        t.setText(s.toUpperCase(L.TR ? new java.util.Locale("tr") : java.util.Locale.ROOT));
        t.setLetterSpacing(0.08f);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
        lp.bottomMargin = px(10);
        parent.addView(t, lp);
        return t;
    }

    private static final int SUB_HEAD = 0xFFB7C0CE;

    private TextView pill(String s, boolean primary) {
        TextView b = text(15, primary ? Ui.ON_ACCENT : Ui.TEXT, true);
        b.setText(s);
        b.setGravity(Gravity.CENTER);
        b.setMinHeight(px(46));
        b.setPadding(px(18), 0, px(18), 0);
        b.setBackground(primary ? Ui.solid(this, 23, Ui.ACCENT) : Ui.glass(this, 23, 0x22FFFFFF, 0x0FFFFFFF, 0x30FFFFFF));
        b.setClickable(true);
        b.setFocusable(true);
        return b;
    }

    private EditText field(String hint, int type) {
        EditText e = new EditText(this);
        e.setHint(hint);
        e.setHintTextColor(0xFF6F7887);
        e.setTextColor(Ui.TEXT);
        e.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        e.setSingleLine(true);
        e.setInputType(type);
        e.setBackground(Ui.field(this));
        e.setPadding(px(14), px(12), px(14), px(12));
        return e;
    }

    private Switch toggle(LinearLayout parent, String label, String pref, boolean def) {
        Switch s = new Switch(this);
        s.setText(label);
        s.setTextColor(Ui.TEXT);
        s.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        s.setMinHeight(px(44));
        s.setChecked(prefs.getBoolean(pref, def));
        s.setThumbTintList(Ui.checkedColors(Color.WHITE, 0xFFB0B7C3));
        s.setTrackTintList(Ui.checkedColors(Ui.ACCENT, 0x55FFFFFF));
        s.setOnCheckedChangeListener((v, on) -> {
            prefs.edit().putBoolean(pref, on).apply();
            MicService.send(this, MicService.ACT_REOPEN);
        });
        parent.addView(s, new LinearLayout.LayoutParams(-1, -2));
        return s;
    }

    private View build() {
        ScrollView scroll = new ScrollView(this);
        scroll.setClipToPadding(false);
        scroll.setFillViewport(true);
        FrameLayout frame = new FrameLayout(this);
        scroll.addView(frame, new ViewGroup.LayoutParams(-1, -2));
        Ui.Column col = new Ui.Column(this, 520);
        frame.addView(col, new FrameLayout.LayoutParams(-1, -2, Gravity.CENTER_HORIZONTAL));
        col.setPadding(px(16), px(12), px(16), px(16));

        scroll.setOnApplyWindowInsetsListener((v, in) -> {
            int l, t, r, b;
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets i = in.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.ime() | WindowInsets.Type.displayCutout());
                l = i.left; t = i.top; r = i.right; b = i.bottom;
            } else {
                l = in.getSystemWindowInsetLeft(); t = in.getSystemWindowInsetTop();
                r = in.getSystemWindowInsetRight(); b = in.getSystemWindowInsetBottom();
            }
            v.setPadding(l, t, r, b);
            return in;
        });

        // başlık
        LinearLayout head = new LinearLayout(this);
        head.setGravity(Gravity.CENTER_VERTICAL);
        head.setPadding(px(2), px(6), 0, px(14));
        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.logo);
        head.addView(logo, new LinearLayout.LayoutParams(px(40), px(40)));
        LinearLayout names = new LinearLayout(this);
        names.setOrientation(LinearLayout.VERTICAL);
        names.setPadding(px(12), 0, 0, 0);
        TextView title = text(20, Ui.TEXT, true);
        title.setText("Broken Microphone");
        TextView sub = text(13, Ui.SUB, false);
        sub.setText(L.t("Telefonun, bilgisayarın mikrofonu", "Your phone as the computer's microphone"));
        names.addView(title);
        names.addView(sub);
        head.addView(names, new LinearLayout.LayoutParams(0, -2, 1));
        col.addView(head);

        // durum + düğme
        LinearLayout main = card(col);
        LinearLayout status = new LinearLayout(this);
        status.setGravity(Gravity.CENTER_VERTICAL);
        dot = new Ui.Dot(this);
        status.addView(dot, new LinearLayout.LayoutParams(px(18), px(18)));
        LinearLayout st = new LinearLayout(this);
        st.setOrientation(LinearLayout.VERTICAL);
        st.setPadding(px(10), 0, 0, 0);
        statusTitle = text(17, Ui.TEXT, true);
        statusDetail = text(14, Ui.SUB, false);
        st.addView(statusTitle);
        st.addView(statusDetail);
        status.addView(st, new LinearLayout.LayoutParams(0, -2, 1));
        main.addView(status);

        trustBtn = pill(L.t("Bu bilgisayara yeniden güven", "Trust this computer again"), false);
        trustBtn.setOnClickListener(v -> {
            Pins.forget(this, Hub.target);
            startMic();
        });
        LinearLayout.LayoutParams tl = new LinearLayout.LayoutParams(-1, -2);
        tl.topMargin = px(12);
        main.addView(trustBtn, tl);

        micBtn = new Ui.MicButton(this);
        micBtn.setOnClickListener(v -> { if (Hub.running) stopMic(); else startMic(); });
        LinearLayout.LayoutParams ml = new LinearLayout.LayoutParams(px(210), px(210));
        ml.gravity = Gravity.CENTER_HORIZONTAL;
        ml.topMargin = px(10);
        main.addView(micBtn, ml);

        micHint = text(14, Ui.SUB, false);
        micHint.setGravity(Gravity.CENTER);
        main.addView(micHint, new LinearLayout.LayoutParams(-1, -2));

        meter = new Ui.Meter(this);
        LinearLayout.LayoutParams mel = new LinearLayout.LayoutParams(-1, px(8));
        mel.topMargin = px(16);
        main.addView(meter, mel);

        muteBtn = pill(L.t("Sustur", "Mute"), false);
        muteBtn.setOnClickListener(v -> MicService.send(this, MicService.ACT_MUTE));
        LinearLayout.LayoutParams mbl = new LinearLayout.LayoutParams(-1, -2);
        mbl.topMargin = px(14);
        main.addView(muteBtn, mbl);

        // bilgisayar
        LinearLayout pc = card(col);
        heading(pc, L.t("Bilgisayar", "Computer"));
        TextView how = text(14, Ui.SUB, false);
        how.setText(L.t(
                "Bilgisayarda Broken Microphone'u aç. QR kodu telefonun kamerasıyla okut ve açılan sayfada \"Uygulamada aç\"a dokun. Ya da pencerede \"Kopyala\"ya basıp buradan yapıştır.",
                "Open Broken Microphone on the computer. Scan the QR code with the phone's camera and tap \"Open in app\" on the page. Or press \"Copy\" in the window and paste it here."));
        pc.addView(how);
        addrField = field(L.t("Adres (192.168.1.5:47820)", "Address (192.168.1.5:47820)"), InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        keyField = field(L.t("Anahtar", "Key"), InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS | InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD);
        String host = prefs.getString("host", "");
        if (host.length() > 0) addrField.setText(host + ":" + prefs.getInt("port", DEFAULT_PORT));
        keyField.setText(prefs.getString("key", ""));
        LinearLayout.LayoutParams fl = new LinearLayout.LayoutParams(-1, -2);
        fl.topMargin = px(12);
        pc.addView(addrField, fl);
        LinearLayout.LayoutParams fl2 = new LinearLayout.LayoutParams(-1, -2);
        fl2.topMargin = px(8);
        pc.addView(keyField, fl2);
        LinearLayout row = new LinearLayout(this);
        TextView paste = pill(L.t("Yapıştır", "Paste"), false);
        paste.setOnClickListener(v -> pasteLink());
        TextView connect = pill(L.t("Bağlan", "Connect"), true);
        connect.setOnClickListener(v -> startMic());
        LinearLayout.LayoutParams half = new LinearLayout.LayoutParams(0, -2, 1);
        row.addView(paste, half);
        LinearLayout.LayoutParams half2 = new LinearLayout.LayoutParams(0, -2, 1);
        half2.leftMargin = px(10);
        row.addView(connect, half2);
        LinearLayout.LayoutParams rl = new LinearLayout.LayoutParams(-1, -2);
        rl.topMargin = px(12);
        pc.addView(row, rl);

        // mikrofon
        LinearLayout mic = card(col);
        heading(mic, L.t("Mikrofon", "Microphone"));
        micSpin = new Spinner(this, Spinner.MODE_DROPDOWN);
        micSpin.setBackground(Ui.field(this));
        micSpin.setPopupBackgroundDrawable(Ui.solid(this, 14, 0xF21B1F27));
        micSpin.setPadding(px(4), px(4), px(4), px(4));
        micSpin.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int pos, long id) {
                if (fillingSpinner) return;
                String key = micKeys.get(pos);
                if (key.equals(prefs.getString("device", ""))) return;
                prefs.edit().putString("device", key).apply();
                needBluetooth();
                MicService.send(MainActivity.this, MicService.ACT_REOPEN);
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        mic.addView(micSpin, new LinearLayout.LayoutParams(-1, px(50)));
        TextView btHint = text(13, Ui.SUB, false);
        btHint.setText(L.t("Kablosuz kulaklık bağlıysa listede görünür.", "A connected wireless headset shows up in the list."));
        btHint.setPadding(px(2), px(8), 0, px(6));
        mic.addView(btHint);
        toggle(mic, L.t("Gürültü azaltma", "Noise suppression"), "ns", true);
        toggle(mic, L.t("Yankı engelleme", "Echo cancellation"), "aec", false);
        toggle(mic, L.t("Otomatik ses seviyesi", "Automatic gain"), "agc", false);

        // alt bilgi
        TextView foot = text(12, 0xFF7D8696, false);
        foot.setText("Broken Microphone · Android " + BuildInfo.VERSION + " · GitHub");
        foot.setGravity(Gravity.CENTER);
        foot.setPadding(0, px(4), 0, px(8));
        foot.setOnClickListener(v -> {
            try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/HErkanuzun/BrokenMicrophone"))); }
            catch (RuntimeException ignored) { }
        });
        col.addView(foot);
        return scroll;
    }

    private void fillMics() {
        List<String> labels = new ArrayList<>();
        micKeys.clear();
        labels.add(L.t("Otomatik", "Automatic"));
        micKeys.add("");
        for (AudioDeviceInfo d : Mics.list(am)) {
            labels.add(Mics.label(d));
            micKeys.add(Mics.key(d));
        }
        String saved = prefs.getString("device", "");
        int sel = Math.max(0, micKeys.indexOf(saved));
        ArrayAdapter<String> a = new ArrayAdapter<String>(this, android.R.layout.simple_spinner_item, labels) {
            @Override
            public View getView(int pos, View convert, ViewGroup parent) {
                TextView t = (TextView) super.getView(pos, convert, parent);
                t.setTextColor(Ui.TEXT);
                t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
                t.setPadding(px(10), 0, px(10), 0);
                return t;
            }
            @Override
            public View getDropDownView(int pos, View convert, ViewGroup parent) {
                TextView t = (TextView) super.getDropDownView(pos, convert, parent);
                t.setTextColor(Ui.TEXT);
                t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
                t.setPadding(px(16), px(14), px(16), px(14));
                return t;
            }
        };
        a.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        fillingSpinner = true;
        micSpin.setAdapter(a);
        micSpin.setSelection(sel, false);
        ui.post(() -> fillingSpinner = false);
    }

    // ------------------------------------------------------------------ durum
    private int micMode() {
        switch (Hub.state) {
            case Hub.LIVE: return Hub.muted ? Ui.MicButton.MUTED : Ui.MicButton.LIVE;
            case Hub.CONNECTING: return Ui.MicButton.CONNECTING;
            case Hub.ERROR: return Ui.MicButton.ERROR;
            default: return Ui.MicButton.IDLE;
        }
    }

    @Override
    public void changed() {
        String target = Hub.running ? Hub.target : currentTargetLabel();
        String t, d;
        int color;
        switch (Hub.state) {
            case Hub.LIVE:
                color = Hub.muted ? Ui.DANGER : Ui.ACCENT;
                t = Hub.muted ? L.t("Susturuldu", "Muted") : L.t("Canlı: ses bilgisayara gidiyor", "Live: audio goes to the computer");
                d = Hub.mic + " → " + target;
                break;
            case Hub.CONNECTING:
                color = Ui.WARN;
                t = L.t("Bağlanıyor…", "Connecting…");
                d = "unreachable".equals(Hub.reason)
                        ? L.t("Bilgisayara ulaşılamıyor. Aynı Wi-Fi'da mısınız, Broken Microphone açık mı?", "Can't reach the computer. Same Wi-Fi? Is Broken Microphone open?")
                        : target;
                break;
            case Hub.ERROR:
                color = Ui.DANGER;
                if ("badKey".equals(Hub.reason)) {
                    t = L.t("Anahtar geçersiz", "Wrong key");
                    d = L.t("Bilgisayardaki QR kodu yeniden okut; anahtar değişmiş olabilir.", "Scan the QR code on the computer again; the key may have changed.");
                } else if ("cert".equals(Hub.reason)) {
                    t = L.t("Bilgisayarın sertifikası değişti", "The computer's certificate changed");
                    d = L.t("Broken Microphone'u yeniden kurduysan ya da ayarlarını sildiysen güvenebilirsin. Değilse ağında başka bir cihaz araya giriyor olabilir.",
                            "If you reinstalled Broken Microphone or cleared its settings, you can trust it. Otherwise another device on the network may be in the way.");
                } else {
                    t = L.t("Mikrofon açılamadı", "Couldn't open the microphone");
                    d = L.t("Başka bir uygulama kullanıyor olabilir, ya da izin verilmedi.", "Another app may be using it, or permission was denied.");
                }
                break;
            default:
                color = Ui.SUB;
                t = L.t("Hazır", "Ready");
                d = target.length() > 0 ? target : L.t("Önce bilgisayarı ekle (aşağıda).", "Add the computer first (below).");
        }
        dot.setColor(color);
        statusTitle.setText(t);
        statusDetail.setText(d);
        trustBtn.setVisibility(Hub.state == Hub.ERROR && "cert".equals(Hub.reason) ? View.VISIBLE : View.GONE);
        muteBtn.setVisibility(Hub.running ? View.VISIBLE : View.GONE);
        muteBtn.setText(Hub.muted ? L.t("Sesi aç", "Unmute") : L.t("Sustur", "Mute"));
        micHint.setText(Hub.running ? L.t("Durdurmak için dokun", "Tap to stop") : L.t("Başlatmak için dokun", "Tap to start"));
        micBtn.set(micMode(), 0);
    }

    private String currentTargetLabel() {
        String h = prefs.getString("host", "");
        return h.length() == 0 ? "" : h + ":" + prefs.getInt("port", DEFAULT_PORT);
    }

    // ------------------------------------------------------------------ bağlantı bilgisi
    private static final Pattern HOST = Pattern.compile("^(?:[a-zA-Z]+://)?([^/:#?\\s]+)(?::(\\d{1,5}))?");
    private static final Pattern KEY = Pattern.compile("[#?&]k=([A-Za-z0-9_\\-]+)");

    /** "https://192.168.1.5:47820/#k=abc", "brokenmic://…/?k=abc" ya da "192.168.1.5:47820" */
    static String[] parse(String s) {
        if (s == null) return null;
        s = s.trim();
        Matcher m = HOST.matcher(s);
        if (!m.find() || m.group(1).isEmpty()) return null;
        String port = m.group(2) == null ? String.valueOf(DEFAULT_PORT) : m.group(2);
        Matcher k = KEY.matcher(s);
        return new String[] { m.group(1), port, k.find() ? k.group(1) : null };
    }

    private void handle(Intent intent) {
        Uri u = intent == null ? null : intent.getData();
        if (u == null || !"brokenmic".equals(u.getScheme())) return;
        String host = u.getHost(), key = u.getQueryParameter("k");
        int port = u.getPort() > 0 ? u.getPort() : DEFAULT_PORT;
        if (host == null || key == null) return;
        addrField.setText(host + ":" + port);
        keyField.setText(key);
        intent.setData(null);
        startMic();
    }

    private void pasteLink() {
        ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        ClipData clip = cm == null ? null : cm.getPrimaryClip();
        CharSequence s = clip != null && clip.getItemCount() > 0 ? clip.getItemAt(0).coerceToText(this) : null;
        String[] t = parse(s == null ? null : s.toString());
        if (t == null) {
            Toast.makeText(this, L.t("Panoda bağlantı yok", "No link on the clipboard"), Toast.LENGTH_SHORT).show();
            return;
        }
        addrField.setText(t[0] + ":" + t[1]);
        if (t[2] != null) keyField.setText(t[2]);
    }

    // ------------------------------------------------------------------ başlat / durdur
    private void startMic() { startMic(true); }

    private void startMic(boolean ask) {
        String[] t = parse(addrField.getText().toString());
        String key = keyField.getText().toString().trim();
        if (t != null && t[2] != null && key.isEmpty()) key = t[2];
        if (t == null || key.isEmpty()) {
            Toast.makeText(this, L.t("Önce adresi ve anahtarı gir (ya da QR kodu okut)", "Enter the address and key first (or scan the QR code)"), Toast.LENGTH_LONG).show();
            return;
        }
        int port;
        try { port = Integer.parseInt(t[1]); } catch (NumberFormatException e) { port = DEFAULT_PORT; }
        prefs.edit().putString("host", t[0]).putInt("port", port).putString("key", key).apply();
        keyField.setText(key);

        List<String> need = new ArrayList<>();
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED)
            need.add(Manifest.permission.RECORD_AUDIO);
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
                && !prefs.getBoolean("askedNotify", false)) {
            need.add(Manifest.permission.POST_NOTIFICATIONS);
            prefs.edit().putBoolean("askedNotify", true).apply();
        }
        if (bluetoothPermissionMissing()) need.add(Manifest.permission.BLUETOOTH_CONNECT);
        if (ask && !need.isEmpty()) {
            pendingStart = true;
            requestPermissions(need.toArray(new String[0]), 1);
            return;
        }
        MicService.start(this, t[0], port, key);
    }

    private void stopMic() {
        MicService.send(this, MicService.ACT_STOP);
    }

    private boolean bluetoothPermissionMissing() {
        if (Build.VERSION.SDK_INT < 31) return false;
        String k = prefs.getString("device", "");
        AudioDeviceInfo d = Mics.find(am, k);
        return d != null && Mics.isBluetooth(d) && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED;
    }

    private void needBluetooth() {
        if (bluetoothPermissionMissing()) requestPermissions(new String[] { Manifest.permission.BLUETOOTH_CONNECT }, 2);
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) {
        if (code == 2) {
            MicService.send(this, MicService.ACT_REOPEN);
            return;
        }
        if (!pendingStart) return;
        pendingStart = false;
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
            startMic(false);
        } else {
            Toast.makeText(this, L.t("Mikrofon izni olmadan ses gönderilemez", "Audio can't be sent without microphone permission"), Toast.LENGTH_LONG).show();
        }
    }
}
