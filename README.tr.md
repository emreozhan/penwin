# PenWin

[English](README.md) | **Türkçe**

iPad + Apple Pencil'ı Windows için kablosuz çizim tableti yapar. iPad'e **uygulama kurulmaz**: Safari'de bir adres açarsınız, kalem hareketleriniz yerel Wi-Fi üzerinden anında PC'ye gider. Sharp3D ve Fusion 360 gibi CAD programlarında fare yerine kalemle çalışmak için yazıldı.

<p align="center">
  <img src="docs/penwin-intro-tr.webp" alt="PenWin intro" width="360">
</p>

### iPad ekranı — Safari'de açılan PenWin sayfası

![iPad'de PenWin: araç çubuğu, monitör oranında çizim alanı ve kalem izi](docs/penwin-ipad.png)

*Görüntü iPad'den (1180×820, yatay). Mavi çizgiler kalemin iPad'deki geçici izi; turuncu artı, havadaki kalemin konumu.*

```
 iPad (Safari)                         Windows PC
 ┌───────────────────┐   WebSocket    ┌──────────────────────────────┐
 │ Pointer Events    │ ── Wi-Fi ───▶  │ penwin.exe                   │
 │ (kalem, basınç,   │  ~1 satır/örn. │  ├─ küçük HTTP + WS sunucusu │
 │  eğim, hover)     │                │  ├─ SendInput   → fare/klavye│
 │ araç çubuğu       │ ◀── hello/pong │  └─ Synthetic Pointer → Ink  │
 └───────────────────┘                └──────────────────────────────┘
```

iPad, Wacom Intuos gibi **ekransız tablet** olarak çalışır: kalemi iPad'de gezdirirken imleci monitörde izlersiniz. Nerede olduğunuzu görmek isterseniz araç çubuğundaki **PC ekranı** anahtarı, seçili monitörün görüntüsünü çizim alanının arkasına yarı saydam olarak saniyede bir kare yerleştirir. iPad'deki çalışma alanı seçilen monitörün en-boy oranındadır ve monitörün tamamına birebir eşlenir.

## İndir

Son sürümü **[Releases](https://github.com/emreozhan/penwin/releases/latest)** sayfasından alın:

- **`PenWin-Setup-x.y.z.exe`** — kurulum (önerilen). Yönetici izni istemeden kullanıcıya kurulur, Başlat menüsüne kısayol ekler (masaüstü kısayolu isteğe bağlı), *Ayarlar → Uygulamalar*'dan kaldırılabilir.
- **`PenWin-x.y.z-portable.zip`** — kurulumsuz: zip'i açıp `penwin.exe`'yi çalıştırın.

Dosyalar imzalı olmadığından Windows SmartScreen ilk açılışta uyarabilir: *Ek bilgi → Yine de çalıştır*.

## Gereksinimler

- Windows 10 (1809+) veya 11. Derleyici (`csc.exe`) .NET Framework 4 ile Windows'ta zaten vardır; ek kurulum gerekmez.
- iPad'de Safari (iPadOS 15.4+). Apple Pencil hover'ı (Pencil Pro / Pencil 2 + destekleyen iPad'ler) varsa imleç dokunmadan önce de hareket eder.
- PC ve iPad aynı yerel ağda olmalı. 5 GHz Wi-Fi gecikmeyi belirgin azaltır.

## Çalıştırma

Başlat menüsünden **PenWin**'i açın (ya da taşınabilir zip'teki `penwin.exe`'yi çalıştırın). Kaynak koddan çalıştırmak için:

```bash
start.cmd
```

`start.cmd` ilk çalıştırmada `build.cmd`'yi çağırır ve `bin\penwin.exe` üretilir. Pencerede şuna benzer bir adres çıkar:

```
  iPad'de Safari ile şu adresi açın:
     http://192.168.1.20:8765/#k=K7M2QX
```

1. Windows Güvenlik Duvarı sorarsa **Özel ağlar** için izin verin.
2. Adresi iPad'de Safari'de açın. Üstteki göstergede **Bağlı** yazmalı.
3. Kalıcı kullanım için Safari'de *Paylaş → Ana Ekrana Ekle*: adres çubuğu olmadan tam ekran açılır, anahtar da hatırlanır.

Anahtar (`#k=...`) `%LOCALAPPDATA%\PenWin\token.txt` içinde saklanır; yeniden derlemede değişmez. Yenilemek için `start.cmd --new-key`.

## Kullanım

**Kalem** — Varsayılan **Fare modu**nda kalem sol tıktır: dokun = tıkla, sürükle = sürükle, havada gezdir = imleci taşı (hover destekleniyorsa).

**Araç çubuğu** (sol tarafta, ayarlardan sağa alınabilir):

| Düğme | Ne yapar |
| --- | --- |
| Sol | Kalem sol tık (varsayılan) |
| Sağ | Sonraki dokunuş sağ tık olur, sonra Sol'a döner |
| Pan | Kalem orta tuşla sürükler (Fusion 360'ta görünümü kaydırır) |
| Orbit | Kalem Shift + orta tuşla sürükler (Fusion 360'ta döndürür) |
| Shift / Ctrl | Basılı kalır, tekrar dokununca bırakılır (çoklu seçim için) |
| Esc, Enter, Sil, Tab | Tuşlar. Tab, Fusion'da ölçü giriş alanları arasında geçer |
| Geri / İleri | Ctrl+Z / Ctrl+Y |
| + / − | Fare tekerleği (zoom) |
| PC ekranı | Açıkken seçili monitörün görüntüsü çizim alanının arkasında yarı saydam görünür (saniyede 1 kare). Görüntü alınamazsa düğmede "alınamadı" yazar (ör. kilit ekranı, UAC penceresi). |

Pan ya da Orbit etkinken aynı düğmeye tekrar dokunmak Sol'a döndürür.

**İki parmak** (ayarlardan kapatılabilir): sürükle = pan (orta tuş), sıkıştır/aç = zoom (tekerlek). Kalem kullanıldıktan hemen sonra gelen parmak/avuç temasları yok sayılır.

**Ayarlar**

*iPad ekranı — Ayarlar penceresi:*

<img src="docs/penwin-ayarlar.png" alt="iPad'de PenWin ayarlar penceresi" width="640">

- **Dil:** Türkçe veya İngilizce. Varsayılan olarak iPad'in dilini izler.
- **Çalışma modu:** *Fare* CAD için önerilir ve her programda çalışır. *Kalem (Windows Ink)* basınç ve eğimi gerçek kalem girdisi olarak gönderir; basınca duyarlı çizim programları içindir. Bu modda Sağ/Pan/Orbit yine fare olarak gider.
- **Ekran:** Birden çok monitör varsa hangisinin eşleneceği.
- **Tıklama ölü bölgesi:** Kalem dokunduktan sonra bu kadar (px) kaymadan hareket gönderilmez. Böylece titreyen bir tık, CAD'de istenmeyen bir sürüklemeye (ör. Fusion'da çizgi yerine yay) dönüşmez. Varsayılan 4 px.
- **PC ekranı saydamlığı:** Arka plan görüntüsünün ne kadar belirgin olacağı (varsayılan %35).
- **Temas eşiği:** Hover desteklemeyen iPad'ler için. Hafif temas yalnızca imleci gezdirir, bu basıncı aşınca tıklar.

## Sorun giderme

| Belirti | Çözüm |
| --- | --- |
| iPad'de sayfa açılmıyor | Güvenlik duvarı iznini kontrol edin (*Windows Güvenlik → Güvenlik duvarı → Bir uygulamaya izin ver* → `penwin.exe`, Özel). Ağ profili *Ortak* ise *Özel* yapın. Misafir Wi-Fi'ları cihazları birbirinden yalıttığı için çalışmayabilir. |
| "Anahtar hatalı" | PC penceresindeki 6 haneli anahtarı Ayarlar'a yazın. |
| "Başka bir cihaz bağlandı" | Aynı anda tek cihaz kontrol eder; son bağlanan kazanır. |
| Bazı pencerelerde hiçbir şey olmuyor | Program yönetici olarak çalışıyorsa Windows dışarıdan girdiye izin vermez. `penwin.exe`'yi de yönetici olarak çalıştırın. |
| İmleç takılıyor / gecikme yüksek | Göstergedeki ms değerine bakın. 5 GHz ağa geçin, iPad'i modeme yaklaştırın. |
| Bağlantı koparsa | Basılı kalan tuş veya düğme kalmaz, sunucu hepsini bırakır. Sayfa kendiliğinden yeniden bağlanır. |

## Sınırlamalar

- Apple Pencil Pro'nun sıkıştırma, çift dokunma ve gövde döndürme hareketleri web sayfalarına açık değil; bunların yerini araç çubuğu tutar.
- Bağlantı yerel ağda şifresiz HTTP/WebSocket'tir. Kontrolü ve ekran görüntüsünü 6 haneli anahtar korur; güvenmediğiniz ağlarda çalıştırmayın.
- Ekran görüntüsü canlı yayın değildir, saniyede bir kare gelir; konum bulmak içindir. Fare imleci görüntüde yoktur, onun yerine iPad'deki turuncu imleç kullanılır.
- Sayfa PC'nin kendi tarayıcısında açılırsa fare olayları bilerek yok sayılır (imleç kendini besleyen bir döngüye girmesin diye).

## Komut satırı

```
penwin.exe [--port 8765] [--bind 0.0.0.0] [--new-key] [--dry-run] [--web <klasör>]
```

- `--port`: dinlenecek port.
- `--bind`: yalnızca belirli bir arayüzü dinler (ör. `127.0.0.1` ile test).
- `--new-key`: yeni bağlantı anahtarı üretir.
- `--dry-run`: gelenleri işler ama Windows'a girdi göndermez; arayüz denemesi içindir.
- `--web`: sayfayı gömülü kaynak yerine diskten sunar (geliştirme).

## Proje yapısı

```
src/Program.cs      giriş, seçenekler, anahtar, konsol
src/WebServer.cs    HTTP + WebSocket (RFC 6455), tek aktif istemci
src/Controller.cs   satır protokolü → fare/kalem/klavye; protokol açıklaması başında
src/Injector.cs     SendInput ve sentetik kalem (InjectSyntheticPointerInput)
src/Monitors.cs     monitör listesi (fiziksel piksel)
src/ScreenCapture.cs  /shot.jpg için monitör görüntüsü (küçültülmüş JPEG)
src/Native.cs       Win32 tanımları
web/                iPad sayfası (HTML, CSS, JS); exe'ye gömülür
web/i18n.js         arayüz metinleri (Türkçe / İngilizce)
installer/penwin.iss  Inno Setup kurulum betiği; .github/workflows v* etiketlerinden release üretir
```

Geliştirirken sayfa değişikliklerini yeniden derlemeden görmek için:

```bash
bin\penwin.exe --dry-run --web web
```

## Lisans

[MIT](LICENSE)
