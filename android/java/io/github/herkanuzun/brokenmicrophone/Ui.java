package io.github.herkanuzun.brokenmicrophone;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.ColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.RadialGradient;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.SystemClock;
import android.view.View;
import android.widget.LinearLayout;

/** Cam (glassmorphism) görünüm: renkli bulanık lekeler üstünde yarı saydam kartlar. */
final class Ui {
    static final int BG = 0xFF0C0E13;
    static final int TEXT = 0xFFEAEDF2;
    static final int SUB = 0xFF9AA3B2;
    static final int ACCENT = 0xFF2EC4B0;
    static final int ACCENT_HI = 0xFF5FE0CD;
    static final int ON_ACCENT = 0xFF06201C;
    static final int DANGER = 0xFFF0565B;
    static final int WARN = 0xFFF5A524;
    static final int GLASS = 0x17FFFFFF;
    static final int GLASS_HI = 0x26FFFFFF;
    static final int GLASS_LINE = 0x2BFFFFFF;

    static float dp(Context c, float v) { return v * c.getResources().getDisplayMetrics().density; }

    /** Kart / alan arka planı: hafif açık üst köşe, ince ışık çizgisi. */
    static GradientDrawable glass(Context c, float radius, int top, int bottom, int line) {
        GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[] { top, bottom });
        g.setCornerRadius(dp(c, radius));
        g.setStroke(Math.max(1, Math.round(dp(c, 1))), line);
        return g;
    }

    static GradientDrawable card(Context c) { return glass(c, 24, GLASS_HI, 0x0AFFFFFF, GLASS_LINE); }

    static GradientDrawable field(Context c) { return glass(c, 14, 0x12FFFFFF, 0x08FFFFFF, 0x22FFFFFF); }

    static GradientDrawable solid(Context c, float radius, int color) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(c, radius));
        return g;
    }

    static ColorStateList checkedColors(int on, int off) {
        return new ColorStateList(new int[][] { { android.R.attr.state_checked }, { } }, new int[] { on, off });
    }

    /** Pencere arka planı: koyu zemin + üç yumuşak renk lekesi (camın arkasında görünen renkler). */
    static final class Backdrop extends Drawable {
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);

        @Override
        public void draw(Canvas c) {
            Rect b = getBounds();
            c.drawColor(BG);
            float w = b.width(), h = b.height(), m = Math.max(w, h);
            blob(c, w * 0.10f, h * 0.08f, m * 0.62f, 0x662EC4B0);
            blob(c, w * 0.95f, h * 0.38f, m * 0.55f, 0x556C5CE7);
            blob(c, w * 0.20f, h * 0.92f, m * 0.60f, 0x442F80ED);
        }

        private void blob(Canvas c, float x, float y, float r, int color) {
            p.setShader(new RadialGradient(x, y, r, color, color & 0x00FFFFFF, Shader.TileMode.CLAMP));
            c.drawCircle(x, y, r, p);
        }

        @Override public void setAlpha(int alpha) { }
        @Override public void setColorFilter(ColorFilter cf) { }
        @Override public int getOpacity() { return PixelFormat.OPAQUE; }
    }

    /** Genişliği sınırlı sütun (tablette ve yatayda ortada durur). */
    static final class Column extends LinearLayout {
        private final int max;

        Column(Context c, int maxDp) {
            super(c);
            setOrientation(VERTICAL);
            max = Math.round(dp(c, maxDp));
        }

        @Override
        protected void onMeasure(int w, int h) {
            int size = MeasureSpec.getSize(w);
            if (size > max) w = MeasureSpec.makeMeasureSpec(max, MeasureSpec.getMode(w) == MeasureSpec.UNSPECIFIED ? MeasureSpec.EXACTLY : MeasureSpec.getMode(w));
            super.onMeasure(w, h);
        }
    }

    /** Durum noktası. */
    static final class Dot extends View {
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        private int color = SUB;

        Dot(Context c) { super(c); }

        void setColor(int c) { if (c != color) { color = c; invalidate(); } }

        @Override
        protected void onDraw(Canvas c) {
            float r = Math.min(getWidth(), getHeight()) / 2f;
            p.setColor(color & 0x40FFFFFF);
            c.drawCircle(getWidth() / 2f, getHeight() / 2f, r, p);
            p.setColor(color);
            c.drawCircle(getWidth() / 2f, getHeight() / 2f, r * 0.5f, p);
        }
    }

    /** Büyük yuvarlak mikrofon düğmesi. Canlıyken sesle birlikte halkası büyür. */
    static final class MicButton extends View {
        static final int IDLE = 0, CONNECTING = 1, LIVE = 2, MUTED = 3, ERROR = 4;
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path path = new Path();
        private final RectF r = new RectF();
        private int mode = IDLE;
        private float level;

        MicButton(Context c) {
            super(c);
            setClickable(true);
            setFocusable(true);
        }

        void set(int mode, float level) {
            this.mode = mode;
            this.level = level;
            invalidate();
        }

        @Override
        protected void onDraw(Canvas c) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f;
            float rad = Math.min(cx, cy) - dp(getContext(), 22);
            float d = dp(getContext(), 1);
            p.setShader(null);
            p.setStyle(Paint.Style.FILL);

            // halo
            if (mode == LIVE) {
                p.setColor(0x332EC4B0);
                c.drawCircle(cx, cy, rad + d * 6 + level * d * 18, p);
                p.setColor(0x1A2EC4B0);
                c.drawCircle(cx, cy, rad + d * 12 + level * d * 26, p);
            } else if (mode == CONNECTING) {
                float t = (SystemClock.uptimeMillis() % 1400) / 1400f;
                p.setColor(Color.argb((int) (90 * (1 - t)), 245, 165, 36));
                c.drawCircle(cx, cy, rad + d * 20 * t + d * 2, p);
            }

            // gövde
            int glyph;
            if (mode == LIVE) {
                p.setShader(new LinearGradient(cx - rad, cy - rad, cx + rad, cy + rad, ACCENT_HI, 0xFF1FA392, Shader.TileMode.CLAMP));
                c.drawCircle(cx, cy, rad, p);
                glyph = ON_ACCENT;
            } else if (mode == MUTED) {
                p.setShader(new LinearGradient(cx - rad, cy - rad, cx + rad, cy + rad, 0xFFFF7A7E, 0xFFD23C42, Shader.TileMode.CLAMP));
                c.drawCircle(cx, cy, rad, p);
                glyph = Color.WHITE;
            } else {
                p.setShader(new LinearGradient(cx - rad, cy - rad, cx + rad, cy + rad, 0x33FFFFFF, 0x0DFFFFFF, Shader.TileMode.CLAMP));
                c.drawCircle(cx, cy, rad, p);
                p.setShader(null);
                p.setStyle(Paint.Style.STROKE);
                p.setStrokeWidth(d * 1.2f);
                p.setColor(mode == ERROR ? DANGER : 0x40FFFFFF);
                c.drawCircle(cx, cy, rad, p);
                glyph = TEXT;
            }
            p.setShader(null);

            // mikrofon simgesi
            float s = rad / 60f;
            p.setColor(glyph);
            p.setStyle(Paint.Style.FILL);
            r.set(cx - 11 * s, cy - 30 * s, cx + 11 * s, cy + 8 * s);
            c.drawRoundRect(r, 11 * s, 11 * s, p);
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(4.5f * s);
            p.setStrokeCap(Paint.Cap.ROUND);
            r.set(cx - 20 * s, cy - 14 * s, cx + 20 * s, cy + 18 * s);
            c.drawArc(r, 0, 180, false, p);
            c.drawLine(cx, cy + 18 * s, cx, cy + 28 * s, p);
            c.drawLine(cx - 11 * s, cy + 29 * s, cx + 11 * s, cy + 29 * s, p);
            if (mode == MUTED) {
                path.reset();
                c.drawLine(cx - 26 * s, cy - 32 * s, cx + 26 * s, cy + 32 * s, p);
            }
            if (mode == CONNECTING || mode == LIVE) postInvalidateOnAnimation();
        }
    }

    /** Ses seviyesi çubuğu (dB ölçeğinde, yavaş düşer). */
    static final class Meter extends View {
        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF r = new RectF();
        private float shown;

        Meter(Context c) { super(c); }

        /** @return gösterilen seviye (0..1) */
        float push(float peak) {
            float v = peak <= 0 ? 0 : (float) ((20 * Math.log10(peak) + 60) / 60);
            v = Math.max(0, Math.min(1, v));
            shown = Math.max(v, shown * 0.88f);
            invalidate();
            return shown;
        }

        @Override
        protected void onDraw(Canvas c) {
            float h = getHeight(), w = getWidth();
            r.set(0, 0, w, h);
            p.setShader(null);
            p.setColor(0x14FFFFFF);
            c.drawRoundRect(r, h / 2, h / 2, p);
            if (shown > 0.005f) {
                r.set(0, 0, Math.max(h, w * shown), h);
                p.setShader(new LinearGradient(0, 0, w, 0, ACCENT, ACCENT_HI, Shader.TileMode.CLAMP));
                c.drawRoundRect(r, h / 2, h / 2, p);
            }
        }
    }

    private Ui() { }
}
