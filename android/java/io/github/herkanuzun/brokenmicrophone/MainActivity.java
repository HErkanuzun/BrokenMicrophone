package io.github.herkanuzun.brokenmicrophone;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
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
import android.widget.CompoundButton;
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

/**
 * Ekranlar: ilk açılışta dil seçimi → ana ekran (büyük mikrofon düğmesi + QR okut) → Ayarlar / QR okuyucu.
 * Bilgisayar QR kodla eklenir; elle giriş Ayarlar'da.
 */
public class MainActivity extends Activity implements Hub.Listener {
    static final int DEFAULT_PORT = 47820;
    private static final int WELCOME = 0, HOME = 1, SETTINGS = 2, SCAN = 3;
    private static final int REQ_START = 1, REQ_BT = 2, REQ_CAMERA = 3;

    private SharedPreferences prefs;
    private AudioManager am;
    private final Handler ui = new Handler(Looper.getMainLooper());

    private FrameLayout root;
    private int screen = -1;

    // ana ekran
    private View home;
    private Ui.Dot dot;
    private TextView statusTitle, statusDetail, muteBtn, trustBtn, scanBtn, pcLine;
    private LinearLayout updateCard;
    private TextView updateText;
    private Ui.MicButton micBtn;
    private Ui.Meter meter;

    // ayarlar
    private Spinner micSpin;
    private final List<String> micKeys = new ArrayList<>();
    private boolean fillingSpinner;
    private TextView versionLine;

    // okuyucu
    private Scanner scanner;

    private boolean pendingStart, resumed;
    private String remoteOk = "";      // ev ağı dışındaki bu adres için kullanıcı onay verdi

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            if (!resumed) return;
            if (screen == HOME && meter != null) {
                float lv = meter.push(Hub.running ? Hub.peak : 0);
                micBtn.set(micMode(), lv);
            }
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
        L.init(this);
        am = (AudioManager) getSystemService(AUDIO_SERVICE);
        Window w = getWindow();
        w.setBackgroundDrawable(new Ui.Backdrop());
        w.setStatusBarColor(Color.TRANSPARENT);
        w.setNavigationBarColor(Color.TRANSPARENT);
        w.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
        root = new FrameLayout(this);
        setContentView(root);
        if (Build.VERSION.SDK_INT >= 33) setRecentsScreenshotEnabled(false);   // bağlantı bilgisi son uygulamalarda görünmesin
        migrateKey();
        show(L.chosen(this) ? HOME : WELCOME);
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
        if (screen == SCAN && scanner != null) scanner.start();
        Updates.check(this, false, (v, ok) -> showUpdate());
        showUpdate();
    }

    @Override
    protected void onPause() {
        resumed = false;
        Hub.remove(this);
        am.unregisterAudioDeviceCallback(deviceWatch);
        if (scanner != null) scanner.stop();
        super.onPause();
    }

    @Override
    public void onBackPressed() {
        if (screen == SETTINGS || screen == SCAN) show(HOME);
        else super.onBackPressed();
    }

    // ------------------------------------------------------------------ ekran geçişi
    private void show(int s) {
        if (scanner != null) { scanner.stop(); scanner = null; }
        setRequestedOrientation(s == SCAN ? ActivityInfo.SCREEN_ORIENTATION_PORTRAIT : ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED);
        View v;
        switch (s) {
            case WELCOME: v = buildWelcome(); break;
            case SETTINGS: v = buildSettings(); break;
            case SCAN: v = buildScan(); break;
            default:
                if (home == null) home = buildHome();
                v = home;
        }
        guard(v);
        root.removeAllViews();
        if (v.getParent() != null) ((ViewGroup) v.getParent()).removeView(v);
        root.addView(v, new FrameLayout.LayoutParams(-1, -1));
        screen = s;
        if (s == HOME) { changed(); showUpdate(); }
        if (s == SCAN && scanner != null && resumed) scanner.start();
    }

    /** Dil değişince her şey yeniden kurulur. */
    private void relang(boolean tr) {
        L.set(this, tr);
        home = null;
        MicService.send(this, MicService.ACT_REOPEN);   // bildirim metni de yeni dilde
    }

    // ------------------------------------------------------------------ yapı taşları
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

    private void heading(LinearLayout parent, String s) {
        TextView t = text(13, 0xFFB7C0CE, true);
        t.setText(s.toUpperCase(L.TR ? new java.util.Locale("tr") : java.util.Locale.ROOT));
        t.setLetterSpacing(0.08f);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
        lp.bottomMargin = px(10);
        parent.addView(t, lp);
    }

    private TextView pill(String s, boolean primary) {
        TextView b = text(15, primary ? Ui.ON_ACCENT : Ui.TEXT, true);
        b.setText(s);
        b.setGravity(Gravity.CENTER);
        b.setMinHeight(px(48));
        b.setPadding(px(18), 0, px(18), 0);
        b.setBackground(primary ? Ui.solid(this, 24, Ui.ACCENT) : Ui.glass(this, 24, 0x22FFFFFF, 0x0FFFFFFF, 0x30FFFFFF));
        b.setClickable(true);
        b.setFocusable(true);
        return b;
    }

    private void add(LinearLayout parent, View v, int topDp) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.topMargin = px(topDp);
        parent.addView(v, lp);
    }

    private ImageView icon(int res, Runnable click) {
        ImageView i = new ImageView(this);
        i.setImageResource(res);
        i.setScaleType(ImageView.ScaleType.CENTER);
        i.setBackground(Ui.glass(this, 22, 0x1AFFFFFF, 0x0AFFFFFF, 0x26FFFFFF));
        i.setClickable(true);
        i.setOnClickListener(v -> click.run());
        return i;
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

    private Switch toggle(LinearLayout parent, String label, boolean on, CompoundButton.OnCheckedChangeListener l) {
        Switch s = new Switch(this);
        s.setText(label);
        s.setTextColor(Ui.TEXT);
        s.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        s.setMinHeight(px(46));
        s.setChecked(on);
        s.setThumbTintList(Ui.checkedColors(Color.WHITE, 0xFFB0B7C3));
        s.setTrackTintList(Ui.checkedColors(Ui.ACCENT, 0x55FFFFFF));
        s.setOnCheckedChangeListener(l);
        parent.addView(s, new LinearLayout.LayoutParams(-1, -2));
        return s;
    }

    private Switch effect(LinearLayout parent, String label, String pref, boolean def) {
        return toggle(parent, label, prefs.getBoolean(pref, def), (v, on) -> {
            prefs.edit().putBoolean(pref, on).apply();
            MicService.send(this, MicService.ACT_REOPEN);
        });
    }

    /** Kaydırılabilir, sistem çubuklarına göre boşluk bırakan, ortalanmış sütun. */
    private Ui.Column page(ScrollView[] out, boolean fill) {
        ScrollView scroll = new ScrollView(this);
        scroll.setClipToPadding(false);
        scroll.setFillViewport(true);
        scroll.setVerticalScrollBarEnabled(false);
        FrameLayout frame = new FrameLayout(this);
        scroll.addView(frame, new ViewGroup.LayoutParams(-1, -1));
        Ui.Column col = new Ui.Column(this, 520);
        frame.addView(col, new FrameLayout.LayoutParams(-1, fill ? -1 : -2, Gravity.CENTER_HORIZONTAL));
        col.setPadding(px(18), px(12), px(18), px(18));
        insets(scroll);
        out[0] = scroll;
        return col;
    }

    private void insets(View v) {
        v.setOnApplyWindowInsetsListener((view, in) -> {
            int l, t, r, b;
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets i = in.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.ime() | WindowInsets.Type.displayCutout());
                l = i.left; t = i.top; r = i.right; b = i.bottom;
            } else {
                l = in.getSystemWindowInsetLeft(); t = in.getSystemWindowInsetTop();
                r = in.getSystemWindowInsetRight(); b = in.getSystemWindowInsetBottom();
            }
            view.setPadding(l, t, r, b);
            return in;
        });
    }

    private View spacer(LinearLayout parent) {
        View s = new View(this);
        parent.addView(s, new LinearLayout.LayoutParams(-1, 0, 1));
        return s;
    }

    // ------------------------------------------------------------------ ilk açılış: dil
    private View buildWelcome() {
        ScrollView[] s = new ScrollView[1];
        Ui.Column col = page(s, true);
        col.setGravity(Gravity.CENTER_HORIZONTAL);
        spacer(col);
        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.logo);
        LinearLayout.LayoutParams ll = new LinearLayout.LayoutParams(px(88), px(88));
        ll.gravity = Gravity.CENTER_HORIZONTAL;
        col.addView(logo, ll);
        TextView title = text(26, Ui.TEXT, true);
        title.setText("Broken Microphone");
        title.setGravity(Gravity.CENTER);
        add(col, title, 18);
        TextView sub = text(15, Ui.SUB, false);
        sub.setText("Dilini seç · Choose your language");
        sub.setGravity(Gravity.CENTER);
        add(col, sub, 6);
        TextView tr = pill("Türkçe", L.TR);
        TextView en = pill("English", !L.TR);
        tr.setOnClickListener(v -> { relang(true); show(HOME); });
        en.setOnClickListener(v -> { relang(false); show(HOME); });
        add(col, tr, 34);
        add(col, en, 12);
        TextView later = text(13, 0xFF7D8696, false);
        later.setText("Ayarlar'dan değiştirebilirsin · You can change it in Settings");
        later.setGravity(Gravity.CENTER);
        add(col, later, 16);
        spacer(col);
        return s[0];
    }

    // ------------------------------------------------------------------ ana ekran
    private View buildHome() {
        ScrollView[] s = new ScrollView[1];
        Ui.Column col = page(s, true);

        // başlık: logo + ad + ayarlar
        LinearLayout head = new LinearLayout(this);
        head.setGravity(Gravity.CENTER_VERTICAL);
        head.setPadding(0, px(6), 0, px(10));
        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.logo);
        head.addView(logo, new LinearLayout.LayoutParams(px(36), px(36)));
        TextView title = text(19, Ui.TEXT, true);
        title.setText("Broken Microphone");
        title.setPadding(px(12), 0, 0, 0);
        head.addView(title, new LinearLayout.LayoutParams(0, -2, 1));
        head.addView(icon(R.drawable.ic_settings, () -> show(SETTINGS)), new LinearLayout.LayoutParams(px(44), px(44)));
        col.addView(head);

        // güncelleme bildirimi
        updateCard = new LinearLayout(this);
        updateCard.setOrientation(LinearLayout.HORIZONTAL);
        updateCard.setGravity(Gravity.CENTER_VERTICAL);
        updateCard.setBackground(Ui.glass(this, 20, 0x332EC4B0, 0x142EC4B0, 0x552EC4B0));
        updateCard.setPadding(px(16), px(10), px(10), px(10));
        updateText = text(14, Ui.TEXT, false);
        updateCard.addView(updateText, new LinearLayout.LayoutParams(0, -2, 1));
        TextView upBtn = pill(L.t("Güncelle", "Update"), true);
        upBtn.setMinHeight(px(40));
        upBtn.setOnClickListener(v -> openUpdate());
        updateCard.addView(upBtn, new LinearLayout.LayoutParams(-2, -2));
        updateCard.setVisibility(View.GONE);
        add(col, updateCard, 4);

        spacer(col);

        micBtn = new Ui.MicButton(this);
        micBtn.setOnClickListener(v -> {
            if (Hub.running) stopMic();
            else if (!hasTarget()) show(SCAN);
            else startMic();
        });
        LinearLayout.LayoutParams ml = new LinearLayout.LayoutParams(px(230), px(230));
        ml.gravity = Gravity.CENTER_HORIZONTAL;
        col.addView(micBtn, ml);

        LinearLayout st = new LinearLayout(this);
        st.setGravity(Gravity.CENTER);
        dot = new Ui.Dot(this);
        st.addView(dot, new LinearLayout.LayoutParams(px(14), px(14)));
        statusTitle = text(19, Ui.TEXT, true);
        statusTitle.setPadding(px(8), 0, 0, 0);
        st.addView(statusTitle);
        add(col, st, 6);
        statusDetail = text(14, Ui.SUB, false);
        statusDetail.setGravity(Gravity.CENTER);
        add(col, statusDetail, 6);

        meter = new Ui.Meter(this);
        LinearLayout.LayoutParams mel = new LinearLayout.LayoutParams(-1, px(6));
        mel.topMargin = px(18);
        mel.leftMargin = mel.rightMargin = px(40);
        col.addView(meter, mel);

        trustBtn = pill(L.t("Bu bilgisayara yeniden güven", "Trust this computer again"), false);
        trustBtn.setOnClickListener(v -> { Pins.forget(this, Hub.target); startMic(); });
        add(col, trustBtn, 16);

        muteBtn = pill(L.t("Sustur", "Mute"), false);
        muteBtn.setOnClickListener(v -> MicService.send(this, MicService.ACT_MUTE));
        add(col, muteBtn, 16);

        spacer(col);

        scanBtn = pill(L.t("QR kodu okut", "Scan QR code"), true);
        scanBtn.setCompoundDrawablesRelativeWithIntrinsicBounds(R.drawable.ic_scan, 0, 0, 0);
        scanBtn.setCompoundDrawablePadding(px(10));
        scanBtn.setMinHeight(px(54));
        scanBtn.setOnClickListener(v -> show(SCAN));
        add(col, scanBtn, 16);

        pcLine = text(13, Ui.SUB, false);
        pcLine.setGravity(Gravity.CENTER);
        add(col, pcLine, 10);
        return s[0];
    }

    private void showUpdate() {
        if (updateCard == null) return;
        String v = Updates.latest(prefs);
        boolean show = Updates.enabled(prefs) && Updates.newer(v);
        updateCard.setVisibility(show ? View.VISIBLE : View.GONE);
        if (show) updateText.setText(L.t("Yeni sürüm hazır: " + v, "New version available: " + v));
        if (versionLine != null) versionLine.setText(versionText());
    }

    private void openUpdate() {
        Toast.makeText(this, L.t("İndirme bitince dosyaya dokun ve Güncelle'ye bas. Ayarların kalır.",
                "When the download finishes, tap the file and press Update. Your settings stay."), Toast.LENGTH_LONG).show();
        open(Updates.APK);
    }

    private void open(String url) {
        try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url))); } catch (RuntimeException ignored) { }
    }

    // ------------------------------------------------------------------ ayarlar
    private View buildSettings() {
        ScrollView[] s = new ScrollView[1];
        Ui.Column col = page(s, false);

        LinearLayout bar = new LinearLayout(this);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setPadding(0, px(6), 0, px(14));
        bar.addView(icon(R.drawable.ic_back, () -> show(HOME)), new LinearLayout.LayoutParams(px(44), px(44)));
        TextView title = text(20, Ui.TEXT, true);
        title.setText(L.t("Ayarlar", "Settings"));
        title.setPadding(px(14), 0, 0, 0);
        bar.addView(title);
        col.addView(bar);

        // dil
        LinearLayout lang = card(col);
        heading(lang, L.t("Dil", "Language"));
        LinearLayout lr = new LinearLayout(this);
        TextView tr = pill("Türkçe", L.TR), en = pill("English", !L.TR);
        tr.setOnClickListener(v -> { if (!L.TR) { relang(true); show(SETTINGS); } });
        en.setOnClickListener(v -> { if (L.TR) { relang(false); show(SETTINGS); } });
        lr.addView(tr, new LinearLayout.LayoutParams(0, -2, 1));
        LinearLayout.LayoutParams h2 = new LinearLayout.LayoutParams(0, -2, 1);
        h2.leftMargin = px(10);
        lr.addView(en, h2);
        lang.addView(lr);

        // bilgisayar
        LinearLayout pc = card(col);
        heading(pc, L.t("Bilgisayar", "Computer"));
        TextView cur = text(15, Ui.TEXT, false);
        cur.setText(hasTarget() ? targetLabel() : L.t("Henüz eklenmedi", "Not added yet"));
        pc.addView(cur);
        TextView scan = pill(L.t("QR kodu okut", "Scan QR code"), true);
        scan.setCompoundDrawablesRelativeWithIntrinsicBounds(R.drawable.ic_scan, 0, 0, 0);
        scan.setCompoundDrawablePadding(px(10));
        scan.setOnClickListener(v -> show(SCAN));
        add(pc, scan, 12);

        final LinearLayout manual = new LinearLayout(this);
        manual.setOrientation(LinearLayout.VERTICAL);
        manual.setVisibility(View.GONE);
        final EditText addr = field(L.t("Adres (192.168.1.5:47820)", "Address (192.168.1.5:47820)"), InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        final EditText key = field(L.t("Anahtar", "Key"), InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        if (hasTarget()) addr.setText(targetLabel());
        add(manual, addr, 12);
        add(manual, key, 8);
        LinearLayout row = new LinearLayout(this);
        TextView paste = pill(L.t("Yapıştır", "Paste"), false);
        paste.setOnClickListener(v -> {
            String[] t = parse(clipboard());
            if (t == null) { Toast.makeText(this, L.t("Panoda bağlantı yok", "No link on the clipboard"), Toast.LENGTH_SHORT).show(); return; }
            addr.setText(t[0] + ":" + t[1]);
            if (t[2] != null) key.setText(t[2]);
        });
        TextView save = pill(L.t("Bağlan", "Connect"), true);
        save.setOnClickListener(v -> {
            String[] t = parse(addr.getText().toString());
            String k = key.getText().toString().trim();
            if (t != null && k.isEmpty() && t[2] != null) k = t[2];
            if (t == null || k.isEmpty()) {
                Toast.makeText(this, L.t("Adresi ve anahtarı gir", "Enter the address and key"), Toast.LENGTH_LONG).show();
                return;
            }
            connectTo(t[0], t[1], k, true);
        });
        row.addView(paste, new LinearLayout.LayoutParams(0, -2, 1));
        LinearLayout.LayoutParams h3 = new LinearLayout.LayoutParams(0, -2, 1);
        h3.leftMargin = px(10);
        row.addView(save, h3);
        add(manual, row, 12);
        TextView manualBtn = pill(L.t("Elle gir", "Enter manually"), false);
        manualBtn.setOnClickListener(v -> { manual.setVisibility(View.VISIBLE); manualBtn.setVisibility(View.GONE); });
        add(pc, manualBtn, 10);
        pc.addView(manual);
        if (hasTarget()) {
            TextView forget = pill(L.t("Bu bilgisayarı unut", "Forget this computer"), false);
            forget.setTextColor(0xFFFF8A8E);
            forget.setOnClickListener(v -> alert(L.t("Bilgisayar unutulsun mu?", "Forget the computer?"),
                    L.t("Adres, anahtar ve sertifika bilgisi silinir. Tekrar bağlanmak için QR kodu okutursun.",
                        "The address, key and certificate are deleted. Scan the QR code to connect again."),
                    L.t("Unut", "Forget"), () -> {
                        stopMic();
                        Pins.forget(this, targetLabel());
                        prefs.edit().remove("host").remove("port").remove("key").apply();
                        show(SETTINGS);
                    }));
            add(pc, forget, 10);
        }

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
                String k = micKeys.get(pos);
                if (k.equals(prefs.getString("device", ""))) return;
                prefs.edit().putString("device", k).apply();
                needBluetooth();
                MicService.send(MainActivity.this, MicService.ACT_REOPEN);
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        mic.addView(micSpin, new LinearLayout.LayoutParams(-1, px(50)));
        fillMics();
        TextView btHint = text(13, Ui.SUB, false);
        btHint.setText(L.t("Kablosuz kulaklık bağlıysa listede görünür.", "A connected wireless headset shows up in the list."));
        btHint.setPadding(px(2), px(8), 0, px(6));
        mic.addView(btHint);
        effect(mic, L.t("Gürültü azaltma", "Noise suppression"), "ns", true);
        effect(mic, L.t("Yankı engelleme", "Echo cancellation"), "aec", false);
        effect(mic, L.t("Otomatik ses seviyesi", "Automatic gain"), "agc", false);

        // güncellemeler
        LinearLayout up = card(col);
        heading(up, L.t("Güncellemeler", "Updates"));
        versionLine = text(15, Ui.TEXT, false);
        versionLine.setText(versionText());
        up.addView(versionLine);
        LinearLayout ur = new LinearLayout(this);
        TextView check = pill(L.t("Şimdi denetle", "Check now"), false);
        check.setOnClickListener(v -> {
            check.setEnabled(false);
            check.setText(L.t("Denetleniyor…", "Checking…"));
            Updates.check(this, true, (latest, ok) -> {
                check.setEnabled(true);
                check.setText(L.t("Şimdi denetle", "Check now"));
                showUpdate();
                if (!ok) Toast.makeText(this, L.t("GitHub'a ulaşılamadı", "Couldn't reach GitHub"), Toast.LENGTH_SHORT).show();
                else if (!Updates.newer(latest)) Toast.makeText(this, L.t("En yeni sürümü kullanıyorsun", "You're on the latest version"), Toast.LENGTH_SHORT).show();
                else openUpdateAsk(latest);
            });
        });
        ur.addView(check, new LinearLayout.LayoutParams(0, -2, 1));
        add(up, ur, 12);
        toggle(up, L.t("Açılışta güncellemeleri denetle", "Check for updates on launch"), Updates.enabled(prefs), (v, on) -> {
            prefs.edit().putBoolean("updates", on).apply();
            showUpdate();
        });
        TextView upNote = text(13, Ui.SUB, false);
        upNote.setText(L.t("Sadece GitHub'daki son sürümün numarasına bakılır; telefondan hiçbir bilgi gönderilmez. Günde en fazla iki kez.",
                "Only the latest version number on GitHub is read; nothing is sent from your phone. At most twice a day."));
        up.addView(upNote);

        // hakkında
        LinearLayout about = card(col);
        heading(about, L.t("Hakkında", "About"));
        TextView page = pill(L.t("İndirme sayfası", "Download page"), false);
        page.setOnClickListener(v -> open(Updates.PAGE));
        about.addView(page);
        TextView src = pill(L.t("Kaynak kod (GitHub)", "Source code (GitHub)"), false);
        src.setOnClickListener(v -> open("https://github.com/HErkanuzun/BrokenMicrophone"));
        add(about, src, 10);
        TextView lic = text(12, 0xFF7D8696, false);
        lic.setText("Broken Microphone · Android " + BuildInfo.VERSION + " · MIT");
        lic.setGravity(Gravity.CENTER);
        add(about, lic, 12);
        return s[0];
    }

    private String versionText() {
        String v = Updates.latest(prefs);
        String s = L.t("Bu sürüm: ", "This version: ") + BuildInfo.VERSION;
        if (!v.isEmpty()) s += Updates.newer(v) ? L.t(" · yeni: ", " · new: ") + v : L.t(" · güncel", " · up to date");
        return s;
    }

    private void openUpdateAsk(String v) {
        alert(L.t("Yeni sürüm: " + v, "New version: " + v),
              L.t("GitHub'dan indirilsin mi? İndirme bitince dosyaya dokun ve Güncelle'ye bas; ayarların kalır.",
                  "Download it from GitHub? When it finishes, tap the file and press Update; your settings stay."),
              L.t("İndir", "Download"), this::openUpdate);
    }

    private void fillMics() {
        if (micSpin == null) return;
        List<String> labels = new ArrayList<>();
        micKeys.clear();
        labels.add(L.t("Otomatik", "Automatic"));
        micKeys.add("");
        for (AudioDeviceInfo d : Mics.list(am)) {
            labels.add(Mics.label(d));
            micKeys.add(Mics.key(d));
        }
        int sel = Math.max(0, micKeys.indexOf(prefs.getString("device", "")));
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

    // ------------------------------------------------------------------ QR okuyucu
    private View buildScan() {
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[] { Manifest.permission.CAMERA }, REQ_CAMERA);
        }
        FrameLayout f = new FrameLayout(this);
        f.setBackgroundColor(0xFF000000);
        scanner = new Scanner(this, this::scanned, () -> {
            if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                Toast.makeText(this, L.t("Kamera açılamadı", "Couldn't open the camera"), Toast.LENGTH_LONG).show();
                show(HOME);
            }
        });
        f.addView(scanner, new FrameLayout.LayoutParams(-1, -1));

        LinearLayout over = new LinearLayout(this);
        over.setOrientation(LinearLayout.VERTICAL);
        over.setPadding(px(18), px(12), px(18), px(28));
        insets(over);
        LinearLayout bar = new LinearLayout(this);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.addView(icon(R.drawable.ic_back, () -> show(HOME)), new LinearLayout.LayoutParams(px(44), px(44)));
        TextView title = text(19, Color.WHITE, true);
        title.setText(L.t("QR kodu okut", "Scan QR code"));
        title.setPadding(px(14), 0, 0, 0);
        bar.addView(title);
        over.addView(bar);
        spacer(over);
        TextView tip = text(15, Color.WHITE, false);
        tip.setGravity(Gravity.CENTER);
        tip.setText(L.t("Bilgisayarda Broken Microphone'u aç ve penceredeki QR kodu çerçeveye getir.",
                "Open Broken Microphone on the computer and fit the QR code in its window into the frame."));
        tip.setShadowLayer(8, 0, 0, 0xCC000000);
        over.addView(tip);
        f.addView(over, new FrameLayout.LayoutParams(-1, -1));
        return f;
    }

    private void scanned(String text) {
        String s = text == null ? "" : text.trim();
        String[] t = s.matches("(?i)^(https|brokenmic)://.*") ? parse(s) : null;
        if (t == null || t[2] == null || !validKey(t[2])) {
            Toast.makeText(this, L.t("Bu bir Broken Microphone QR kodu değil", "That's not a Broken Microphone QR code"), Toast.LENGTH_SHORT).show();
            ui.postDelayed(() -> { if (scanner != null) scanner.again(); }, 1200);
            return;
        }
        show(HOME);
        connectTo(t[0], t[1], t[2], true);
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
        if (home == null || statusTitle == null) return;
        String target = Hub.running ? Hub.target : targetLabel();
        String t, d;
        int color;
        switch (Hub.state) {
            case Hub.LIVE:
                color = Hub.muted ? Ui.DANGER : Ui.ACCENT;
                t = Hub.muted ? L.t("Susturuldu", "Muted") : L.t("Canlı", "Live");
                d = Hub.muted ? L.t("Bilgisayar seni duymuyor", "The computer can't hear you") : Hub.mic + " → " + target;
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
                    d = L.t("Bilgisayardaki QR kodu yeniden okut.", "Scan the QR code on the computer again.");
                } else if ("cert".equals(Hub.reason)) {
                    t = L.t("Sertifika değişti", "Certificate changed");
                    d = L.t("Broken Microphone'u yeniden kurduysan güvenebilirsin. Değilse ağında başka bir cihaz araya giriyor olabilir.",
                            "If you reinstalled Broken Microphone you can trust it. Otherwise another device on the network may be in the way.");
                } else {
                    t = L.t("Mikrofon açılamadı", "Couldn't open the microphone");
                    d = L.t("Başka bir uygulama kullanıyor olabilir ya da izin verilmedi.", "Another app may be using it, or permission was denied.");
                }
                break;
            default:
                color = Ui.SUB;
                if (hasTarget()) {
                    t = L.t("Hazır", "Ready");
                    d = L.t("Başlatmak için mikrofona dokun", "Tap the microphone to start");
                } else {
                    t = L.t("Hoş geldin", "Welcome");
                    d = L.t("Bilgisayarda Broken Microphone'u aç ve QR kodunu okut.", "Open Broken Microphone on the computer and scan its QR code.");
                }
        }
        dot.setColor(color);
        statusTitle.setText(t);
        statusDetail.setText(d);
        trustBtn.setVisibility(Hub.state == Hub.ERROR && "cert".equals(Hub.reason) ? View.VISIBLE : View.GONE);
        muteBtn.setVisibility(Hub.running ? View.VISIBLE : View.GONE);
        muteBtn.setText(Hub.muted ? L.t("Sesi aç", "Unmute") : L.t("Sustur", "Mute"));
        boolean has = hasTarget();
        scanBtn.setVisibility(Hub.running ? View.GONE : View.VISIBLE);
        scanBtn.setText(has ? L.t("Başka bilgisayar: QR okut", "Another computer: scan QR") : L.t("QR kodu okut", "Scan QR code"));
        scanBtn.setBackground(has ? Ui.glass(this, 24, 0x22FFFFFF, 0x0FFFFFFF, 0x30FFFFFF) : Ui.solid(this, 24, Ui.ACCENT));
        scanBtn.setTextColor(has ? Ui.TEXT : Ui.ON_ACCENT);
        if (scanBtn.getCompoundDrawablesRelative()[0] != null) scanBtn.getCompoundDrawablesRelative()[0].mutate().setTint(has ? Ui.TEXT : Ui.ON_ACCENT);
        pcLine.setText(has ? L.t("Bilgisayar: ", "Computer: ") + targetLabel() : "");
        pcLine.setVisibility(has ? View.VISIBLE : View.GONE);
        micBtn.set(micMode(), 0);
    }

    private boolean hasTarget() { return !prefs.getString("host", "").isEmpty() && !prefs.getString("key", "").isEmpty(); }

    private String targetLabel() {
        String h = prefs.getString("host", "");
        return h.isEmpty() ? "" : h + ":" + prefs.getInt("port", DEFAULT_PORT);
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

    /**
     * Sayfadaki "Uygulamada aç" bağlantısı (brokenmic://adres:port/?k=anahtar). Herhangi bir web sitesi de böyle bir
     * bağlantı açabileceği için: sadece ev ağındaki adresler kabul edilir ve bağlanmadan önce kullanıcıya sorulur.
     */
    private void handle(Intent intent) {
        Uri u = intent == null ? null : intent.getData();
        if (u == null || !"brokenmic".equals(u.getScheme())) return;
        intent.setData(null);
        final String host = u.getHost(), key = u.getQueryParameter("k");
        final int port = u.getPort() > 0 ? u.getPort() : DEFAULT_PORT;
        if (host == null || key == null || !validKey(key) || port > 65535) return;
        if (!L.chosen(this)) L.set(this, L.TR);         // bağlantıyla ilk açılış: telefonun dili
        if (screen != HOME) show(HOME);
        if (!isLocal(host)) {
            alert(L.t("Bağlantı açılmadı", "Link not opened"),
                  L.t("Bu bağlantı ev ağındaki bir bilgisayarı göstermiyor (" + host + "). Güvenlik için uygulama sadece aynı ağdaki bilgisayarlara bağlanır.",
                      "This link doesn't point to a computer on your home network (" + host + "). For safety the app only connects to computers on the same network."),
                  null, null);
            return;
        }
        alert(L.t("Bu bilgisayara bağlanılsın mı?", "Connect to this computer?"),
              host + ":" + port + "\n\n" + L.t("Mikrofonunun sesi bu bilgisayara gidecek. Bu bağlantıyı bilgisayardaki Broken Microphone sayfasından açtıysan Bağlan'a dokun.",
                                                 "Your microphone's audio will go to this computer. Tap Connect only if you opened this from the Broken Microphone page on your computer."),
              L.t("Bağlan", "Connect"), () -> connectTo(host, String.valueOf(port), key, true));
    }

    private void alert(String title, String msg, String okText, Runnable ok) {
        AlertDialog.Builder b = new AlertDialog.Builder(this, android.R.style.Theme_Material_Dialog_Alert)
                .setTitle(title).setMessage(msg);
        if (ok != null) {
            b.setPositiveButton(okText, (d, w) -> ok.run());
            b.setNegativeButton(L.t("Vazgeç", "Cancel"), null);
        } else {
            b.setPositiveButton(L.t("Tamam", "OK"), null);
        }
        AlertDialog d = b.show();
        View pos = d.getButton(AlertDialog.BUTTON_POSITIVE);
        if (pos != null) pos.setFilterTouchesWhenObscured(true);
    }

    static boolean validKey(String k) {
        return k != null && k.matches("[A-Za-z0-9_\\-]{4,64}");
    }

    /** Ev ağı adresi mi: 10/8, 172.16/12, 192.168/16, 169.254/16, 100.64/10 (Tailscale vb.), IPv6 yerel, .local/.lan/.home.arpa */
    static boolean isLocal(String host) {
        if (host == null) return false;
        String h = host.toLowerCase(java.util.Locale.ROOT);
        if (h.endsWith(".local") || h.endsWith(".lan") || h.endsWith(".home.arpa")) return true;
        if (h.startsWith("[")) h = h.substring(1, h.length() - 1);
        if (h.contains(":")) return h.startsWith("fe80:") || h.startsWith("fc") || h.startsWith("fd");
        String[] p = h.split("\\.");
        if (p.length != 4) return false;
        int[] n = new int[4];
        try {
            for (int i = 0; i < 4; i++) { n[i] = Integer.parseInt(p[i]); if (n[i] < 0 || n[i] > 255) return false; }
        } catch (NumberFormatException e) { return false; }
        return n[0] == 10
                || (n[0] == 172 && n[1] >= 16 && n[1] <= 31)
                || (n[0] == 192 && n[1] == 168)
                || (n[0] == 169 && n[1] == 254)
                || (n[0] == 100 && n[1] >= 64 && n[1] <= 127);
    }

    /** Dokunma kaçırma koruması: üstte başka bir pencere varken dokunuşlar yok sayılır. */
    private static void guard(View v) {
        v.setFilterTouchesWhenObscured(true);
        if (v instanceof ViewGroup)
            for (int i = 0; i < ((ViewGroup) v).getChildCount(); i++) guard(((ViewGroup) v).getChildAt(i));
    }

    private void migrateKey() {
        String s = prefs.getString("key", "");
        if (!s.isEmpty() && !s.startsWith("v1:")) prefs.edit().putString("key", Secret.seal(s)).apply();   // eski düz kayıt: şifrele
    }

    private String loadKey() {
        String s = prefs.getString("key", "");
        return s.startsWith("v1:") ? Secret.open(s) : s;
    }

    private String clipboard() {
        ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        ClipData clip = cm == null ? null : cm.getPrimaryClip();
        CharSequence s = clip != null && clip.getItemCount() > 0 ? clip.getItemAt(0).coerceToText(this) : null;
        return s == null ? null : s.toString();
    }

    // ------------------------------------------------------------------ başlat / durdur
    /** Bilgisayarı doğrula, kaydet ve başlat. Ev ağı dışındaki adres için açık onay ister. */
    private void connectTo(String host, String portStr, String key, boolean start) {
        if (!validKey(key)) {
            Toast.makeText(this, L.t("Anahtar geçersiz", "Invalid key"), Toast.LENGTH_LONG).show();
            return;
        }
        int port;
        try { port = Integer.parseInt(portStr); } catch (NumberFormatException e) { port = DEFAULT_PORT; }
        if (port <= 0 || port > 65535) port = DEFAULT_PORT;
        if (!isLocal(host) && !host.equals(remoteOk)) {
            alert(L.t("Ev ağı dışında bir adres", "Address outside your home network"),
                  host + "\n\n" + L.t("Bu adres ev ağında görünmüyor. Mikrofonunun sesi bu adrese gidecek. Emin misin?",
                                       "This address doesn't look like your home network. Your microphone's audio will go there. Are you sure?"),
                  L.t("Yine de bağlan", "Connect anyway"), () -> { remoteOk = host; connectTo(host, portStr, key, start); });
            return;
        }
        if (Hub.running && !(host + ":" + port).equals(Hub.target)) stopMic();
        prefs.edit().putString("host", host).putInt("port", port).putString("key", Secret.seal(key)).apply();
        if (screen != HOME) show(HOME); else changed();
        if (start) startMic();
    }

    private void startMic() { startMic(true); }

    private void startMic(boolean ask) {
        String host = prefs.getString("host", "");
        String key = loadKey();
        if (host.isEmpty() || key.isEmpty()) { show(SCAN); return; }
        int port = prefs.getInt("port", DEFAULT_PORT);

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
            requestPermissions(need.toArray(new String[0]), REQ_START);
            return;
        }
        MicService.start(this, host, port, key);
    }

    private void stopMic() {
        MicService.send(this, MicService.ACT_STOP);
    }

    private boolean bluetoothPermissionMissing() {
        if (Build.VERSION.SDK_INT < 31) return false;
        AudioDeviceInfo d = Mics.find(am, prefs.getString("device", ""));
        return d != null && Mics.isBluetooth(d) && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED;
    }

    private void needBluetooth() {
        if (bluetoothPermissionMissing()) requestPermissions(new String[] { Manifest.permission.BLUETOOTH_CONNECT }, REQ_BT);
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) {
        if (code == REQ_BT) {
            MicService.send(this, MicService.ACT_REOPEN);
            return;
        }
        if (code == REQ_CAMERA) {
            if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                if (screen == SCAN && scanner != null) scanner.start();
            } else {
                Toast.makeText(this, L.t("QR okumak için kamera izni gerekli. İstersen Ayarlar'dan elle de girebilirsin.",
                        "Camera permission is needed to scan. You can also enter it manually in Settings."), Toast.LENGTH_LONG).show();
                show(HOME);
            }
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
