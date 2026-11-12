# MemoryPulse privacy policy

*Effective: 12 November 2026* · [Türkçe](#memorypulse-gizlilik-politikası)

MemoryPulse does not collect, sell or share personal data. It has no accounts, no ads, no analytics and no telemetry.

**What it reads, on your PC only.** Memory figures from Windows, and the names, memory use, file locations and (for script hosts and shared runtimes) command lines of running programs, to show them in its window. Paths and command lines are only shown on screen. The *Disk space* window reads the names, sizes and dates of files and folders on the drive or folder you choose, while a scan runs; they are shown on screen only and are not stored or logged.

**What it stores, on your PC only.** In `%APPDATA%\MemoryPulse\`: `settings.json` (your settings) and `memorypulse.log` (start-up, settings changes, low-memory events with the names and memory use of the largest programs). The log is capped at about 1 MB. With *Record usage history* on (default), `history\` holds one file per day with, every minute, memory figures, disk and network speed for the whole PC, free disk space, and the names, memory and read/write amounts of the largest programs (network amounts only for programs you chose to watch). Files older than 7 days are deleted automatically. Crash and restart events are read from Windows' own event logs when you open *History*; MemoryPulse does not copy them. To remove everything, delete this folder.

**Network.**
- Microsoft Store edition: makes no network connections.
- GitHub edition: connects only when you click *Check for updates*. It then asks GitHub (`api.github.com`) for the number of the latest release. Nothing about your PC is sent; as with any web request, GitHub sees the IP address of the request ([GitHub privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)).

**Children.** MemoryPulse collects no data from anyone, including children.

**Changes.** Changes to this policy are published in this file; its history is in the repository.

**Contact.** Open an issue at https://github.com/ahmetunaluzun/MemoryPulse/issues.

---

# MemoryPulse gizlilik politikası

*Yürürlük: 12 Kasım 2026*

MemoryPulse kişisel veri toplamaz, satmaz, paylaşmaz. Hesap, reklam, analiz ve telemetri yoktur.

**Ne okur (yalnız bilgisayarınızda).** Windows'tan bellek değerlerini; çalışan programların adını, bellek kullanımını, dosya konumunu ve (betik çalıştırıcıları ile ortak çalışma ortamlarında) komut satırını, penceresinde göstermek için. Yollar ve komut satırları yalnız ekranda gösterilir. *Disk alanı* penceresi, tarama sürerken seçtiğiniz sürücüdeki ya da klasördeki dosya ve klasörlerin adını, boyutunu ve tarihini okur; bunlar yalnız ekranda gösterilir, saklanmaz ve loga yazılmaz.

**Ne saklar (yalnız bilgisayarınızda).** `%APPDATA%\MemoryPulse\` klasöründe: `settings.json` (ayarlarınız) ve `memorypulse.log` (açılış, ayar değişiklikleri, düşük bellek olayları ve o anda en çok bellek kullanan programların adı ve bellek miktarı). Log yaklaşık 1 MB ile sınırlıdır. *Kullanım geçmişini kaydet* açıksa (varsayılan) `history\` klasöründe her gün için bir dosya tutulur: dakikada bir bellek değerleri, bilgisayarın tamamındaki disk ve ağ hızı, disklerdeki boş alan ve en çok kullanan programların adı, belleği ve okuma/yazma miktarı (ağ miktarı yalnız izlemeyi seçtiğiniz programlar için). 7 günden eski dosyalar kendiliğinden silinir. Çökme ve yeniden başlatma olayları *Geçmiş* açıldığında Windows'un kendi olay kayıtlarından okunur; MemoryPulse bunları kopyalamaz. Her şeyi silmek için bu klasörü silin.

**Ağ.**
- Microsoft Store sürümü: hiçbir ağ bağlantısı kurmaz.
- GitHub sürümü: yalnız *Güncellemeleri kontrol et*'e tıkladığınızda bağlanır ve GitHub'a (`api.github.com`) en son sürümün numarasını sorar. Bilgisayarınız hakkında hiçbir bilgi gönderilmez; GitHub, her web isteğinde olduğu gibi isteğin IP adresini görür ([GitHub gizlilik bildirimi](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)).

**Çocuklar.** MemoryPulse çocuklar dahil kimseden veri toplamaz.

**Değişiklikler.** Bu politikadaki değişiklikler bu dosyada yayımlanır; geçmişi depodadır.

**İletişim.** https://github.com/ahmetunaluzun/MemoryPulse/issues adresinde bir kayıt açın.
