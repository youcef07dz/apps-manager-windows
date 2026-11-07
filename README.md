# Apps Manager

One small window to see, update and remove every program on Windows.
No background service, no telemetry, one exe and one HTML page.

**Languages:** [English](#english) · [Français](#français) · [العربية](#العربية)

---

## English

### What it is

Apps Manager is a native Windows desktop app that lists everything installed on the PC
and lets you uninstall it, update it through winget, or install something new from the
winget catalog. It uses a single .NET exe plus one HTML page, so it starts fast and
stays light on RAM and CPU.

### Features

- Installed programs with version, publisher, install date and **real folder size**
- Uninstall, run, open install folder, copy path, open registry key
- Update one app or **Update all** through winget
- **Check updates** against the winget catalog; ignored updates stay ignored
- **App installer** tab: search winget and install packages without leaving the app
- **Microsoft** tab (Store and system components) and **Missing** tab
- Startup column: shows which programs start at sign-in
- Export the list to CSV (safe for Excel)
- Search, publisher filter, sortable columns, light/dark theme
- Interface in **English, French and Arabic**
- Self-update from the title bar: downloads the new setup with aria2c, checks its
  SHA-256, then runs it
- winget ships packed and is unpacked on first use, so the app takes less disk

### Requirements

- Windows 10 version 1809 (build 17763) or later.
- .NET Framework 4.8 (already installed on up-to-date Windows 10/11).
- Internet only for updates, installs and catalog checks.

### Install

1. Download the setup from the [releases](releases) folder:
   - **Lite** (about 2.4 MB): downloads winget when you first need it.
   - **Full** (about 59 MB): includes packed winget, works offline.
2. Run it and follow the wizard. It installs to `C:\Program Files\Apps Manager`
   and asks for administrator rights once.
3. Start **Apps Manager** from the Start menu or the desktop shortcut.

### Use

- The tabs on the left switch between All programs, Updates, Microsoft, Missing and
  App installer.
- Click a row for details; right-click for the action menu.
- **Sizes** measures the real folder of every program. It can take a minute; cancel
  any time.
- **Check update** in the title bar updates Apps Manager itself.

### How updates work

`version.json` in this repo is the update feed:

```json
{
  "version": "1.1",
  "url": "https://raw.githubusercontent.com/youcef07dz/apps-manager-windows/main/releases/AppsManager-Setup-1.1-Lite.exe",
  "sha256": "435e621a0b521b7a8ca75dee9142fbceae4ed244bce74aaebcef10d11a0dab10",
  "size": 2425406
}
```

The app accepts only HTTPS links, requires a 64-character SHA-256, and deletes the
downloaded file if the hash does not match. To publish a new version, build it, copy
the setup into `releases/`, update `version.json`, and run `push.cmd`.

### Build from source

Needs the .NET Framework compiler and Inno Setup 5.6 in `C:\inno`.

```bat
build.cmd          :: 64-bit exe  -> bin\AppsManager.exe
build.cmd x86      :: 32-bit exe  -> bin\x86\AppsManager.exe
build-setup.cmd    :: installers  -> dist\
push.cmd           :: commit and push everything to GitHub
```

### Repository layout

| Path | What it is |
| --- | --- |
| `app.html` | the whole UI (IE8-safe HTML/CSS/JS) |
| `*.cs` | app logic: scanning, winget, downloads, self-update |
| `lang/` | English, French and Arabic strings |
| `tools/aria2c.exe` | downloader used for winget and updates |
| `tools/winget.zip` | packed winget, unpacked on first use |
| `version.json` | update feed |
| `releases/` | published setups |
| `setup.iss` | Inno Setup script |

---

## Français

### C'est quoi

Apps Manager est une application Windows native qui liste tout ce qui est installé
sur le PC et permet de désinstaller, mettre à jour avec winget, ou installer de
nouveaux programmes depuis le catalogue winget. Un seul exe .NET et une page HTML :
démarrage rapide, peu de RAM et de CPU.

### Fonctions

- Liste des programmes avec version, éditeur, date d'installation et **taille réelle**
- Désinstaller, lancer, ouvrir le dossier, copier le chemin, ouvrir la clé de registre
- Mettre à jour un programme ou **Tout mettre à jour** avec winget
- **Vérifier les mises à jour** via le catalogue winget, avec mises à jour ignorées
- Onglet **App installer** : chercher et installer des paquets winget
- Onglets **Microsoft** (Store et composants système) et **Manquants**
- Colonne démarrage : quels programmes se lancent à l'ouverture de session
- Export CSV compatible Excel
- Recherche, filtre par éditeur, colonnes triables, thème clair/sombre
- Interface en **anglais, français et arabe**
- Mise à jour de l'application depuis la barre de titre : téléchargement avec aria2c,
  vérification SHA-256, puis lancement du programme d'installation
- winget est livré compressé et décompressé à la première utilisation

### Configuration requise

- Windows 10 version 1809 (build 17763) ou plus.
- .NET Framework 4.8 (déjà présent sur Windows 10/11 à jour).
- Internet uniquement pour les mises à jour, installations et vérifications.

### Installation

1. Téléchargez le setup dans [releases](releases) :
   - **Lite** (environ 2,4 Mo) : télécharge winget au premier besoin.
   - **Full** (environ 59 Mo) : contient winget compressé, fonctionne hors ligne.
2. Lancez-le et suivez l'assistant. Il s'installe dans `C:\Program Files\Apps Manager`
   et demande les droits administrateur une fois.
3. Ouvrez **Apps Manager** depuis le menu Démarrer ou le raccourci du bureau.

### Utilisation

- Les onglets à gauche passent de Tous les programmes à Mises à jour, Microsoft,
  Manquants et App installer.
- Cliquez une ligne pour les détails ; clic droit pour le menu d'actions.
- **Tailles** mesure le dossier réel de chaque programme. Cela peut prendre une
  minute ; annulable à tout moment.
- **Vérifier la mise à jour** dans la barre de titre met à jour Apps Manager.

### Compiler

```bat
build.cmd          :: exe 64 bits -> bin\AppsManager.exe
build.cmd x86      :: exe 32 bits -> bin\x86\AppsManager.exe
build-setup.cmd    :: installateurs -> dist\
push.cmd           :: commit et envoi sur GitHub
```

---

## العربية

<div dir="rtl">

### ما هو التطبيق

Apps Manager تطبيق سطح مكتب أصلي لنظام ويندوز يعرض كل البرامج المثبّتة على الجهاز،
ويتيح إزالة تثبيتها أو تحديثها عبر winget أو تثبيت برامج جديدة من كتالوج winget.
يتكوّن من ملف exe واحد (.NET) وصفحة HTML واحدة، لذلك يبدأ بسرعة ويستهلك ذاكرة
ومعالجًا قليلين.

### المزايا

- قائمة البرامج مع الإصدار والناشر وتاريخ التثبيت و**الحجم الحقيقي للمجلد**
- إزالة التثبيت، التشغيل، فتح مجلد التثبيت، نسخ المسار، فتح مفتاح التسجيل
- تحديث برنامج واحد أو **تحديث الكل** عبر winget
- **التحقق من التحديثات** مقابل كتالوج winget، مع تجاهل التحديثات التي تختارها
- تبويب **مثبّت التطبيقات**: البحث في winget وتثبيت الحزم
- تبويب **مايكروسوفت** (متجر ومكوّنات النظام) وتبويب **مفقودة**
- عمود بدء التشغيل: البرامج التي تعمل عند تسجيل الدخول
- تصدير القائمة إلى CSV متوافق مع Excel
- بحث وتصفية حسب الناشر وأعمدة قابلة للفرز ومظهر فاتح/داكن
- الواجهة بـ **الإنجليزية والفرنسية والعربية**
- تحديث التطبيق نفسه من شريط العنوان: تنزيل باستخدام aria2c ثم التحقق من SHA-256
  ثم تشغيل المثبّت
- يأتي winget مضغوطًا ويُفك عند أول استخدام لتوفير مساحة القرص

### المتطلبات

- ويندوز 10 إصدار 1809 (بناء 17763) أو أحدث.
- .NET Framework 4.8 (مثبّت مسبقًا على ويندوز 10/11 المحدّث).
- الإنترنت مطلوب فقط للتحديثات والتثبيت والتحقق من الكتالوج.

### التثبيت

1. نزّل المثبّت من مجلد [releases](releases):
   - **Lite** (حوالي 2.4 ميجابايت): ينزّل winget عند أول حاجة.
   - **Full** (حوالي 59 ميجابايت): يحتوي winget مضغوطًا ويعمل دون إنترنت.
2. شغّله واتبع المعالج. يثبّت في `C:\Program Files\Apps Manager` ويطلب صلاحيات
   المدير مرة واحدة.
3. افتح **Apps Manager** من قائمة ابدأ أو من اختصار سطح المكتب.

### الاستخدام

- تبويبات اليسار تنقلك بين كل البرامج والتحديثات ومايكروسوفت والمفقودة ومثبّت
  التطبيقات.
- انقر صفًا لعرض التفاصيل، أو انقر بالزر الأيمن لقائمة الإجراءات.
- **الأحجام** يقيس المجلد الحقيقي لكل برنامج، وقد يستغرق دقيقة، ويمكن إلغاؤه.
- **تحقق من التحديث** في شريط العنوان يحدّث Apps Manager نفسه.

### البناء من المصدر

```bat
build.cmd          :: نسخة 64 بت -> bin\AppsManager.exe
build.cmd x86      :: نسخة 32 بت -> bin\x86\AppsManager.exe
build-setup.cmd    :: المثبّتات -> dist\
push.cmd           :: الحفظ والرفع إلى GitHub
```

</div>
