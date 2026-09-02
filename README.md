# Worker Attendance Machines (.NET 8)

Console tool untuk menarik log punch ZKTeco, dedup/queue di SQLite, push ke `deneire-cms` dengan protokol ADMS, provisioning karyawan, scan LAN, hapus log, dan sinkronisasi waktu. Ini adalah port C# dari tool Python dengan CLI yang sama.

## Prasyarat Windows

- App berjalan sebagai proses **32-bit (x86)** karena `zkemkeeper.dll` dari ZKTeco Standalone SDK adalah COM component 32-bit-only — Windows tidak bisa memuat in-proc COM server 32-bit ke proses 64-bit. Windows 32-bit maupun 64-bit keduanya bisa dipakai, asalkan ada akses LAN ke mesin (biasanya TCP port `4370`).
- Official ZKTeco **Standalone SDK** harus di-install pada setiap PC yang menjalankan tool. COM component `zkemkeeper.dll` harus terdaftar (installer vendor biasanya melakukan ini; jika perlu jalankan regsvr32 versi 32-bit sebagai administrator: `%windir%\SysWOW64\regsvr32.exe zkemkeeper.dll` di Windows 64-bit).
- Serial number yang diisi lewat menu Settings harus sama persis dengan mesin yang didaftarkan di CMS.

SDK proprietary tidak disertakan dalam release. Aplikasi memakai late binding `zkemkeeper.CZKEM`, sehingga build/CI tidak membutuhkan DLL SDK; operasi device akan memberi pesan jelas bila dijalankan non-Windows atau COM belum terdaftar.

## Konfigurasi dan penggunaan

Jalankan aplikasi tanpa argumen. Pada first run, aplikasi meminta URL CMS, meminta konfirmasi, lalu menawarkan scan LAN untuk memilih mesin yang akan didaftarkan. Pengaturan dan daftar mesin disimpan di `attendance.db`, di folder yang sama dengan log (di samping executable bila writable, atau `%LOCALAPPDATA%\AttendanceAgent`). Semuanya dapat diubah kemudian lewat menu **8. Settings**.

Jika `config.json` lama ditemukan di working directory, URL CMS, capacity warning, dan entri mesin yang valid diimpor otomatis. Setelah berhasil, file tersebut di-rename menjadi `config.json.imported` dan wizard dilewati.

```powershell
attendance-agent.exe fetch [--machine "Mesin Lantai 1"]
attendance-agent.exe export [--machine NAME] [--from 2025-01-01] [--to 2025-01-31] --out laporan.csv
attendance-agent.exe delete --machine NAME [--force]
attendance-agent.exe status [--machine NAME]
attendance-agent.exe sync-users --machine NAME
attendance-agent.exe scan [--subnet 192.168.1] [--port 4370]
attendance-agent.exe update-time [--machine NAME]
```

Tanpa argumen, aplikasi menampilkan menu interaktif. `delete` ditolak bila masih ada queue yang belum terkirim kecuali `--force`. Log berada di `attendance-agent.log` di samping executable, atau `%LOCALAPPDATA%\AttendanceAgent` bila folder aplikasi tidak writable.

## Build, test, publish

```powershell
dotnet build
dotnet test
dotnet publish src/AttendanceAgent/AttendanceAgent.csproj -c Release -r win-x86 --self-contained -p:PublishSingleFile=true -o publish/attendance-agent
```

Publish pakai `PublishSingleFile=true` — hasilnya cuma 3 file (`attendance-agent.exe`, `.pdb`, dan `e_sqlite3.dll`) alih-alih ~200 DLL berserakan. `IncludeNativeLibrariesForSelfExtract` sengaja dibiarkan default (`false`), jadi satu-satunya native dependency (`e_sqlite3.dll` — SQLite) tetap jadi file biasa di sebelah exe, **bukan** di-embed lalu diekstrak ke `%TEMP%` saat runtime — jadi tetap aman dari blokir Windows Application Control tanpa perlu folder-based `--onedir`-style publish. Untuk Task Scheduler, gunakan argument `fetch`; lokasi database dan log tidak bergantung pada working directory.

## Verifikasi hardware wajib

Signature/metode mengikuti surface `ICZKEM` Standalone SDK yang umum. Constants `GetDeviceStatus` dan signature user/device-info harus dicocokkan dengan dokumentasi/type library versi SDK yang benar-benar dipasang. Lakukan smoke test di Windows pada LAN mesin nyata: `status`, `scan`, lalu `fetch`; verifikasi kapasitas, serial, punch type, timestamp WIB, push CMS, dan guard `delete` sebelum produksi.
