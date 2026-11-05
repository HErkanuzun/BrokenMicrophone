package io.github.herkanuzun.brokenmicrophone;

import android.media.AudioDeviceInfo;
import android.media.AudioManager;

import java.util.ArrayList;
import java.util.List;

/** Telefonun mikrofon girişleri: dahili, kablolu, USB, Bluetooth kulaklık. */
final class Mics {
    static final int TYPE_BLE_HEADSET = 26;   // AudioDeviceInfo.TYPE_BLE_HEADSET (Android 12)

    static List<AudioDeviceInfo> list(AudioManager am) {
        List<AudioDeviceInfo> r = new ArrayList<>();
        boolean builtin = false;
        for (AudioDeviceInfo d : am.getDevices(AudioManager.GET_DEVICES_INPUTS)) {
            switch (d.getType()) {
                case AudioDeviceInfo.TYPE_BUILTIN_MIC:
                    if (builtin) continue;      // alt/arka mikrofon ayrı görünür; tek satır yeter
                    builtin = true;
                    break;
                case AudioDeviceInfo.TYPE_WIRED_HEADSET:
                case AudioDeviceInfo.TYPE_USB_DEVICE:
                case AudioDeviceInfo.TYPE_USB_HEADSET:
                case AudioDeviceInfo.TYPE_BLUETOOTH_SCO:
                case TYPE_BLE_HEADSET:
                    break;
                default:
                    continue;
            }
            r.add(d);
        }
        return r;
    }

    static String key(AudioDeviceInfo d) {
        return d.getType() + ":" + d.getProductName();
    }

    static AudioDeviceInfo find(AudioManager am, String key) {
        if (key == null || key.isEmpty()) return null;
        for (AudioDeviceInfo d : list(am)) if (key(d).equals(key)) return d;
        return null;
    }

    static boolean isBluetooth(AudioDeviceInfo d) {
        return d.getType() == AudioDeviceInfo.TYPE_BLUETOOTH_SCO || d.getType() == TYPE_BLE_HEADSET;
    }

    static String label(AudioDeviceInfo d) {
        String name = d.getProductName() == null ? "" : d.getProductName().toString();
        switch (d.getType()) {
            case AudioDeviceInfo.TYPE_BUILTIN_MIC: return L.t("Telefonun mikrofonu", "Phone microphone");
            case AudioDeviceInfo.TYPE_WIRED_HEADSET: return L.t("Kablolu kulaklık", "Wired headset");
            case AudioDeviceInfo.TYPE_USB_DEVICE:
            case AudioDeviceInfo.TYPE_USB_HEADSET: return "USB · " + name;
            default: return "Bluetooth · " + name;
        }
    }

    private Mics() { }
}
