# R8: uygulamanın kendi sınıfları olduğu gibi kalır; ZXing'den sadece QR okuma için gerekenler alınır.
-keep class io.github.herkanuzun.brokenmicrophone.** { *; }
-dontobfuscate
-dontwarn **
