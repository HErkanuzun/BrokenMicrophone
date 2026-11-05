package io.github.herkanuzun.brokenmicrophone;

import android.util.Base64;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.SocketTimeoutException;
import java.net.URLEncoder;
import java.security.GeneralSecurityException;
import java.security.SecureRandom;

import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

/**
 * Bilgisayardaki Broken Microphone'a WebSocket bağlantısı (TLS üzerinden).
 * Telefon sayfasıyla aynı dil: metin = JSON durum, ikili = 16 bit mono PCM.
 */
final class WsClient {
    static final class BadKey extends IOException {
        BadKey() { super("bad key"); }
    }

    private final SSLSocket sock;
    private final OutputStream out;
    private final InputStream in;
    private final SecureRandom rnd = new SecureRandom();
    private final byte[] mask = new byte[4];
    private byte[] frame = new byte[8192];
    private volatile boolean closed;
    private volatile long lastPong = System.currentTimeMillis();

    private WsClient(SSLSocket s) throws IOException {
        sock = s;
        out = new BufferedOutputStream(s.getOutputStream(), 16384);
        in = new BufferedInputStream(s.getInputStream(), 4096);
    }

    static WsClient open(String host, int port, String key, String userAgent, X509TrustManager trust) throws IOException {
        SSLContext ctx;
        try {
            ctx = SSLContext.getInstance("TLS");
            ctx.init(null, new TrustManager[] { trust }, null);
        } catch (GeneralSecurityException e) {
            throw new IOException(e);
        }
        SSLSocket s = (SSLSocket) ctx.getSocketFactory().createSocket();
        try {
            s.connect(new InetSocketAddress(host, port), 5000);
            s.setTcpNoDelay(true);
            s.setSoTimeout(8000);
            s.startHandshake();
            WsClient c = new WsClient(s);
            byte[] nonce = new byte[16];
            c.rnd.nextBytes(nonce);
            String req = "GET /ws?k=" + URLEncoder.encode(key, "UTF-8") + " HTTP/1.1\r\n"
                    + "Host: " + host + ":" + port + "\r\n"
                    + "Upgrade: websocket\r\n"
                    + "Connection: Upgrade\r\n"
                    + "Sec-WebSocket-Key: " + Base64.encodeToString(nonce, Base64.NO_WRAP) + "\r\n"
                    + "Sec-WebSocket-Version: 13\r\n"
                    + "User-Agent: " + userAgent + "\r\n\r\n";
            c.out.write(req.getBytes("UTF-8"));
            c.out.flush();
            String status = c.line();
            while (c.line().length() > 0) { /* başlıkları atla */ }
            if (status.contains(" 403")) throw new BadKey();
            if (!status.contains(" 101")) throw new IOException("unexpected answer: " + status);
            s.setSoTimeout(1000);
            return c;
        } catch (IOException e) {
            try { s.close(); } catch (IOException ignored) { }
            throw e;
        }
    }

    private String line() throws IOException {
        StringBuilder b = new StringBuilder();
        while (true) {
            int ch = in.read();
            if (ch < 0) throw new EOFException();
            if (ch == '\n') return b.toString();
            if (ch != '\r') b.append((char) ch);
            if (b.length() > 8192) throw new IOException("header too long");
        }
    }

    /** Bir çerçeve gönderir (istemciden giden her çerçeve maskelenir). Ses ve durum aynı anda yazabilir. */
    synchronized void send(int op, byte[] data, int off, int len) throws IOException {
        if (closed) throw new IOException("closed");
        int head = len < 126 ? 2 : (len < 65536 ? 4 : 10);
        int total = head + 4 + len;
        if (frame.length < total) frame = new byte[total];
        frame[0] = (byte) (0x80 | op);
        if (len < 126) {
            frame[1] = (byte) (0x80 | len);
        } else if (len < 65536) {
            frame[1] = (byte) (0x80 | 126);
            frame[2] = (byte) (len >> 8);
            frame[3] = (byte) len;
        } else {
            frame[1] = (byte) (0x80 | 127);
            for (int i = 0; i < 8; i++) frame[2 + i] = (byte) (i < 4 ? 0 : len >> (8 * (7 - i)));
        }
        rnd.nextBytes(mask);
        System.arraycopy(mask, 0, frame, head, 4);
        int p = head + 4;
        for (int i = 0; i < len; i++) frame[p + i] = (byte) (data[off + i] ^ mask[i & 3]);
        out.write(frame, 0, total);
        out.flush();
    }

    void text(String s) throws IOException {
        byte[] b = s.getBytes("UTF-8");
        send(1, b, 0, b.length);
    }

    void ping() throws IOException { send(9, new byte[0], 0, 0); }

    /**
     * Gelen çerçeveleri okur; bağlantı kopunca ya da bilgisayar 8 sn yanıt vermeyince döner/atar.
     * Bilgisayar sadece ping'lere pong ile yanıt verir.
     */
    void pump() throws IOException {
        byte[] h = new byte[8];
        while (!closed) {
            int first;
            try {
                first = in.read();
            } catch (SocketTimeoutException e) {
                checkAlive();
                continue;
            }
            if (first < 0) throw new EOFException();
            readFully(h, 0, 1);
            int op = first & 0x0F;
            long len = h[0] & 0x7F;
            if (len == 126) {
                readFully(h, 0, 2);
                len = ((h[0] & 0xff) << 8) | (h[1] & 0xff);
            } else if (len == 127) {
                readFully(h, 0, 8);
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | (h[i] & 0xff);
            }
            if (len > 1 << 20) throw new IOException("frame too big");
            byte[] payload = new byte[(int) len];
            readFully(payload, 0, payload.length);
            if (op == 10) lastPong = System.currentTimeMillis();
            else if (op == 8) throw new EOFException("closed by computer");
        }
    }

    private void checkAlive() throws IOException {
        if (System.currentTimeMillis() - lastPong > 8000) throw new IOException("computer stopped answering");
    }

    private void readFully(byte[] b, int off, int len) throws IOException {
        while (len > 0) {
            int n;
            try {
                n = in.read(b, off, len);
            } catch (SocketTimeoutException e) {
                checkAlive();
                continue;
            }
            if (n < 0) throw new EOFException();
            off += n;
            len -= n;
        }
    }

    void close() {
        if (closed) return;
        try { send(8, new byte[0], 0, 0); } catch (IOException ignored) { }
        closed = true;
        try { sock.close(); } catch (IOException ignored) { }
    }
}
