package io.github.herkanuzun.brokenmicrophone;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.SurfaceTexture;
import android.hardware.Camera;
import android.os.Handler;
import android.os.HandlerThread;
import android.view.Surface;
import android.view.TextureView;
import android.view.View;
import android.view.WindowManager;
import android.widget.FrameLayout;

import com.google.zxing.BarcodeFormat;
import com.google.zxing.BinaryBitmap;
import com.google.zxing.DecodeHintType;
import com.google.zxing.PlanarYUVLuminanceSource;
import com.google.zxing.ReaderException;
import com.google.zxing.common.HybridBinarizer;
import com.google.zxing.qrcode.QRCodeReader;

import java.util.Collections;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;

/**
 * Uygulama içi QR okuyucu: arka kamera önizlemesi + çerçeve. Görüntü telefonda çözülür (ZXing), hiçbir yere gönderilmez.
 * Kamera sadece bu ekran açıkken çalışır.
 */
@SuppressWarnings("deprecation")   // android.hardware.Camera: her Android sürümünde çalışır, ek kütüphane gerektirmez
final class Scanner extends FrameLayout implements TextureView.SurfaceTextureListener, Camera.PreviewCallback {
    interface Found { void found(String text); }
    interface Failed { void failed(); }

    private final TextureView tex;
    private final Found onFound;
    private final Failed onFailed;
    private final QRCodeReader reader = new QRCodeReader();
    private final Map<DecodeHintType, Object> hints = new EnumMap<>(DecodeHintType.class);
    private Camera cam;
    private int pw, ph, rot;
    private boolean autoFocusLoop;
    private HandlerThread worker;
    private Handler wh;
    private volatile boolean done;
    private final Runnable focus = new Runnable() {
        @Override public void run() {
            if (cam == null || !autoFocusLoop) return;
            try { cam.autoFocus(null); } catch (RuntimeException ignored) { }
            postDelayed(this, 2000);
        }
    };

    Scanner(Context c, Found onFound, Failed onFailed) {
        super(c);
        this.onFound = onFound;
        this.onFailed = onFailed;
        hints.put(DecodeHintType.POSSIBLE_FORMATS, Collections.singletonList(BarcodeFormat.QR_CODE));
        hints.put(DecodeHintType.TRY_HARDER, Boolean.TRUE);
        setBackgroundColor(0xFF000000);
        tex = new TextureView(c);
        tex.setSurfaceTextureListener(this);
        addView(tex, new LayoutParams(-1, -1));
        addView(new Frame(c), new LayoutParams(-1, -1));
    }

    /** Ekran tekrar görünür olunca (onResume). */
    void start() {
        done = false;
        if (tex.isAvailable()) open(tex.getSurfaceTexture());
    }

    /** Ekrandan çıkarken (onPause / geri). */
    void stop() { close(); }

    /** Okunan kod bizim değilse taramaya devam. */
    void again() {
        done = false;
        if (cam != null) try { cam.setOneShotPreviewCallback(this); } catch (RuntimeException ignored) { }
    }

    @Override public void onSurfaceTextureAvailable(SurfaceTexture st, int w, int h) { open(st); }
    @Override public void onSurfaceTextureSizeChanged(SurfaceTexture st, int w, int h) { transform(); }
    @Override public boolean onSurfaceTextureDestroyed(SurfaceTexture st) { close(); return true; }
    @Override public void onSurfaceTextureUpdated(SurfaceTexture st) { }

    private void open(SurfaceTexture st) {
        if (cam != null) return;
        try {
            int id = -1;
            Camera.CameraInfo info = new Camera.CameraInfo();
            for (int i = 0; i < Camera.getNumberOfCameras(); i++) {
                Camera.getCameraInfo(i, info);
                if (info.facing == Camera.CameraInfo.CAMERA_FACING_BACK) { id = i; break; }
            }
            if (id < 0 && Camera.getNumberOfCameras() > 0) { id = 0; Camera.getCameraInfo(0, info); }
            if (id < 0) throw new RuntimeException("no camera");
            cam = Camera.open(id);
            Camera.Parameters p = cam.getParameters();
            Camera.Size best = null;
            for (Camera.Size s : p.getSupportedPreviewSizes()) {
                if (s.width < 640) continue;
                if (best == null || Math.abs(s.width * s.height - 1280 * 720) < Math.abs(best.width * best.height - 1280 * 720)) best = s;
            }
            if (best != null) p.setPreviewSize(best.width, best.height);
            List<String> modes = p.getSupportedFocusModes();
            autoFocusLoop = false;
            if (modes != null && modes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE)) {
                p.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
            } else if (modes != null && modes.contains(Camera.Parameters.FOCUS_MODE_AUTO)) {
                p.setFocusMode(Camera.Parameters.FOCUS_MODE_AUTO);
                autoFocusLoop = true;
            }
            cam.setParameters(p);
            Camera.Size s = cam.getParameters().getPreviewSize();
            pw = s.width;
            ph = s.height;

            int deg = 0;
            WindowManager wm = (WindowManager) getContext().getSystemService(Context.WINDOW_SERVICE);
            switch (wm.getDefaultDisplay().getRotation()) {
                case Surface.ROTATION_90: deg = 90; break;
                case Surface.ROTATION_180: deg = 180; break;
                case Surface.ROTATION_270: deg = 270; break;
            }
            rot = info.facing == Camera.CameraInfo.CAMERA_FACING_FRONT
                    ? (360 - (info.orientation + deg) % 360) % 360
                    : (info.orientation - deg + 360) % 360;
            cam.setDisplayOrientation(rot);
            cam.setPreviewTexture(st);
            cam.startPreview();
            transform();
            worker = new HandlerThread("qr");
            worker.start();
            wh = new Handler(worker.getLooper());
            if (autoFocusLoop) post(focus);
            cam.setOneShotPreviewCallback(this);
        } catch (Exception e) {
            close();
            if (onFailed != null) onFailed.failed();
        }
    }

    private void close() {
        removeCallbacks(focus);
        if (cam != null) {
            try { cam.setPreviewCallback(null); cam.stopPreview(); } catch (RuntimeException ignored) { }
            cam.release();
            cam = null;
        }
        if (worker != null) { worker.quitSafely(); worker = null; wh = null; }
    }

    /** Önizlemeyi ekranı dolduracak şekilde ortadan kırp (bozulmadan). */
    private void transform() {
        int vw = tex.getWidth(), vh = tex.getHeight();
        if (vw == 0 || vh == 0 || pw == 0) return;
        float cw = rot % 180 == 90 ? ph : pw, ch = rot % 180 == 90 ? pw : ph;
        float content = cw / ch, view = vw / (float) vh, sx = 1, sy = 1;
        if (content > view) sx = content / view; else sy = view / content;
        Matrix m = new Matrix();
        m.setScale(sx, sy, vw / 2f, vh / 2f);
        tex.setTransform(m);
    }

    @Override
    public void onPreviewFrame(final byte[] data, Camera camera) {
        final Handler h = wh;
        if (done || h == null) return;
        final int w = pw, hh = ph;
        h.post(() -> {
            String text = decode(data, w, hh);
            post(() -> {
                if (cam == null || done) return;
                if (text != null) {
                    done = true;
                    performHapticFeedback(android.view.HapticFeedbackConstants.CONFIRM);
                    onFound.found(text);
                } else {
                    try { cam.setOneShotPreviewCallback(this); } catch (RuntimeException ignored) { }
                }
            });
        });
    }

    private String decode(byte[] data, int w, int h) {
        if (data == null || data.length < w * h) return null;
        int side = (int) (Math.min(w, h) * 0.9f);
        String r = read(new PlanarYUVLuminanceSource(data, w, h, (w - side) / 2, (h - side) / 2, side, side, false));
        // kod çok yakındaysa kenarları kırpılmış olabilir: bütün kareyi de dene
        return r != null ? r : read(new PlanarYUVLuminanceSource(data, w, h, 0, 0, w, h, false));
    }

    private String read(PlanarYUVLuminanceSource src) {
        try {
            return reader.decode(new BinaryBitmap(new HybridBinarizer(src)), hints).getText();
        } catch (ReaderException | RuntimeException e) {
            return null;
        } finally {
            reader.reset();
        }
    }

    /** Karartılmış kenarlar + ortada köşeli çerçeve. */
    private static final class Frame extends View {
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path path = new Path();
        private final RectF r = new RectF();

        Frame(Context c) { super(c); }

        @Override
        protected void onDraw(Canvas c) {
            float w = getWidth(), h = getHeight(), side = Math.min(w, h) * 0.68f;
            float l = (w - side) / 2, t = (h - side) / 2 - h * 0.04f;
            r.set(l, t, l + side, t + side);
            float rad = Ui.dp(getContext(), 22);
            path.reset();
            path.setFillType(Path.FillType.EVEN_ODD);
            path.addRect(0, 0, w, h, Path.Direction.CW);
            path.addRoundRect(r, rad, rad, Path.Direction.CW);
            p.setStyle(Paint.Style.FILL);
            p.setColor(0x99000000);
            c.drawPath(path, p);
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(Ui.dp(getContext(), 4));
            p.setStrokeCap(Paint.Cap.ROUND);
            p.setColor(Ui.ACCENT_HI);
            float k = side * 0.16f;
            // dört köşe
            corner(c, r.left, r.top, k, k, rad);
            corner(c, r.right, r.top, -k, k, rad);
            corner(c, r.left, r.bottom, k, -k, rad);
            corner(c, r.right, r.bottom, -k, -k, rad);
        }

        private void corner(Canvas c, float x, float y, float dx, float dy, float rad) {
            Path q = new Path();
            q.moveTo(x, y + dy);
            q.lineTo(x, y + Math.signum(dy) * rad);
            q.quadTo(x, y, x + Math.signum(dx) * rad, y);
            q.lineTo(x + dx, y);
            c.drawPath(q, p);
        }
    }
}
