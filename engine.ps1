<#
  AutoRejoin — движок (engine.ps1).

  Его запускает приложение AutoRejoin.exe — скрыто, без окна консоли. Команды приходят строками в stdin:
    stop | reload | rejoin-all | rejoin <ключ аккаунта> | cancel-reboot | schedule-now
  а в stdout движок пишет строки JSON: {"t":"log",...} (журнал), {"t":"status",...} (состояние окон, раз в секунду),
  {"t":"alert",...} (то, что без тебя не починить), {"t":"auth",...} (куку аккаунта пора заменить: 401 / 403 —
  приложение берёт новую само, если вход по логину и паролю). Настройки — config.json, аккаунты и логин Потассиума — vault.dat
  (зашифрован Windows DPAPI под текущего пользователя: прочитать его можно только под этой учётной записью на этом ПК)
  в папке данных (-DataDir, по умолчанию %LOCALAPPDATA%\AutoRejoin). Изменения в них движок подхватывает сам.

  При запуске сам открывает Потассиум (если он включён в настройках и ещё не открыт) и заводит в игру все аккаунты,
  у которых нет открытого окна, — по одному: следующее окно запускается, только когда предыдущее зашло на сервер
  (и, если включена проверка скрипта, в нём запустился скрипт: загрузчик в автоэкзекуте Потассиума сообщает об этом
  файлом в workspace) и прошло ещё $LaunchCooldown сек. Код VIP-сервера share-ссылки запоминается в links.cache.json,
  так что истёкшая share-ссылка запуску не мешает. Роблокс запускается напрямую, минуя Froststrap/Bloxstrap: так на
  2–3 сек быстрее; вышла новая версия Роблокса — этот запуск идёт через них, они её и скачают.
  Дальше движок держит аккаунты в игре. Окно аккаунта перезапускается, если оно:
    - закрылось или вылетело;
    - вылетело с сервера: кик, 20 минут бездействия, потеря связи — сразу, по коду вылета в логе Роблокса
      (обычный прыжок на другой сервер вылетом не считается);
    - зависло ("Не отвечает");
    - после запуска так и не зашло на сервер;
    - раздулось в памяти или в системе почти не осталось свободной памяти;
    - упало с окном ошибки ("Roblox encountered an unexpected error", Windows "прекращена работа программы" и т.п.);
    - показало в игре ошибку Роблокса (о ней сообщает скрипт в окне);
    - скрипт в окне не запустился вовремя или перестал работать (нет "пульса");
    - Balloon Blessing мейна продлился N раз (если включено; по логу данных Atlas) — тогда перезаходят ВСЕ окна, сразу
      после продления (шар уже сдан); ближе $BalloonQuietMinutes мин к плановому перезапуску — нет, его дождёмся.
  Потассиум: автоаттач не подключился к окну за $ManualAttachDelay сек после захода — движок жмёт Attach сам (раз на окно);
  Потассиум ответил "Channel build mismatch" — движок закрывает окна Роблокса (кроме того, к которому шёл аттач: его
  Потассиум закрывает сам) и жмёт "Fix it", с открытыми окнами Потассиум клиент не заменит.
  Хелперам (не мейну) можно поставить потолок RAM: через минуту после захода окну ставится жёсткий предел рабочего
  набора и низкий приоритет памяти, лишнее Windows выгружает в подкачку, а окну, которому тесно, потолок поднимается
  сам (RejoinMemory). И минимальную графику (SavedQualityLevel) — мейну при запуске возвращается его собственная.
  Новое окно открывается, только когда для него хватает памяти. Аккаунт с устаревшей кукой (401) или который Роблокс
  не пускает (403 "User is moderated" — непринятое предупреждение или бан) не задерживает остальные: движок просит
  приложение взять ему новую куку и перепроверяет его раз в 10 (вход по паролю) или 30 минут. Окна аккаунтов, которых
  нет в списке, не трогаются.
  Плановый перезапуск: каждый день в заданное время (в выбранном часовом поясе) — перезагрузка компьютера или
  перезаход всеми аккаунтами.

  -SetupPagefile — выставить файл подкачки под нужное число окон (-AccountCount) и выйти. Приложение запускает так
  кнопку "Настроить подкачку"; права администратора движок попросит сам, потом нужна перезагрузка.
#>
param(
    [string]$DataDir = '',          # папка данных приложения; пусто — %LOCALAPPDATA%\AutoRejoin
    [int]$StartupDelay = 0,         # подождать столько сек перед стартом (автозапуск при включении ПК: даём Windows прогрузиться)
    [switch]$SetupPagefile,         # выставить файл подкачки под число аккаунтов и выйти
    [int]$AccountCount = 0,         # для -SetupPagefile: сколько окон держать
    [switch]$Elevated,              # для -SetupPagefile: это уже процесс с правами администратора
    [string]$InstanceName = 'Local\RobloxAutoRejoin_Watchdog'   # мьютекс "движок уже запущен" (другое имя — только для проверок)
)

# Настройки по умолчанию. config.json приложения их переопределяет (Set-EngineConfig) — и при старте, и на ходу.
$Link            = ''    # ссылка для аккаунтов без своей: https://www.roblox.com/share?code=XXXX&type=Server (VIP),
                         # .../games/1537690962/...?privateServerLinkCode=XXXX, ссылка на игру или просто ID плейса
$LinkFile        = ''    # файл, из которого брать ссылку (первая ссылка Роблокса в нём, меняется на лету); пусто — не брать
$DefaultPlace    = '1537690962'   # ссылки нет вовсе — публичный сервер выбранной игры (по умолчанию Bee Swarm Simulator)
$DefaultGameName = 'Bee Swarm Simulator'
$script:GameNames = @{}       # своя игра аккаунта: ID плейса -> название (для журнала)

$CheckInterval   = 1     # как часто проверять окна, сек (движок при этом занимает ~2–3% одного ядра)
$LaunchTimeout   = 120   # сколько ждать, пока откроется окно (с учётом обновления Роблокса)
$JoinTimeout     = 60    # сколько ждать захода на сервер после запуска (обычно 4–9 сек)
$OutOfGameGrace  = 15    # сколько ждать после непонятного дисконнекта (прыжок на другой сервер занимает ~0.2 сек;
                         # настоящий вылет — кик, бездействие, потеря связи — видно по логу сразу, без ожидания)
$HangTimeout     = 60    # сколько секунд "Не отвечает" терпеть до перезапуска
$RelaunchDelay   = 1     # пауза перед перезаходом, сек (растёт при неудачах подряд, до 5 минут)
$LaunchGap       = 2     # минимальная пауза между запусками разных аккаунтов, сек

$MemoryLimitGB   = 8     # окно съело больше — перезапуск (не раньше 2 мин после старта); 0 — не следить
$MemoryLimitSkipMain = $false # мейна по $MemoryLimitGB не перезапускать (только хелперов)
$WindowMemoryGB  = 3.5   # сколько обычно занимает окно: без столько свободной памяти новое окно не открывается; 0 — не ждать
$MinFreeMemoryGB = 1.5   # свободной памяти (вместе с файлом подкачки) меньше — перезапуск самого большого окна; 0 — не следить
$ScriptStartTimeout = 0  # через столько сек после захода в окне должен запуститься скрипт (сообщает загрузчик в автоэкзекуте
                         # Потассиума); не запустился — перезапуск окна. Пока скрипт не запустился, следующее окно ждёт. 0 — не проверять
$ScriptTimeout   = 0     # столько сек нет пульса скрипта (загрузчик пишет его раз в 10 сек) — перезапуск; 0 — не следить
$LaunchCooldown  = 3     # пауза после запуска скрипта в окне, перед запуском следующего окна, сек
$PotassiumPath   = ''    # где лежит Potassium.exe; пусто — ищется сам (запущенный, прошлый раз, Рабочий стол, Загрузки)
$PotassiumWarmup = 3     # сколько сек дать Потассиуму загрузиться (он стартует за ~1 сек)
$PotassiumAutoLogin = $false   # входить в Потассиум самому (логин и пароль — из vault.dat), когда он покажет экран входа
$KeepAutoAttach  = $true # держать автоаттач Потассиума включённым (Settings -> Client -> Auto attach)
$ManualAttachDelay = 25  # автоаттач не подключился к окну за столько сек после захода — жмём Attach сами (Invoke-ManualAttach); 0 — не жать

$MainMaxBackoff  = 2     # мейн при неудачном заходе пробует снова не реже, чем раз в столько сек
$MainFirstMax    = 90    # пока мейн не в игре со скриптом, остальные окна не запускаются — но не дольше стольких сек подряд
                         # (мейн не заходит из-за бана или куки — хелперы не должны стоять)

$NoPotassium     = $false # не запускать экзекьютор самому и не следить за ним
$Executor        = 'potassium' # чем подключать скрипт к окнам: 'potassium', 'real' (Real Executor), 'volt', 'synapsez'
$ExecutorPath    = ''          # папка (или exe) Volt / Synapse Z, если сам не нашёл
$AutoReboot      = $false # новые окна умирают сразу после старта (лечится только перезагрузкой) — перезагрузить ПК сразу,
                          # не чаще раза в 30 минут; после перезагрузки всё запустится само (нужен автозапуск приложения)
$ViaBootstrapper = $false # запускать Роблокс через Froststrap/Bloxstrap (медленнее)
$WindowWidth     = 800   # до какого размера уменьшать окно хелпера после захода
$WindowHeight    = 600
$NoMinimize      = $true  # не уменьшать и не сворачивать окна хелперов (с 3.8.3 по умолчанию не сворачиваем)
$MainFps         = 60    # лимит FPS мейна; 0 — не трогать
$HelperFps       = 30    # лимит FPS хелперов; 0 — не трогать
$MainPriority    = ''    # приоритет ЦП окна мейна (Idle, BelowNormal, Normal, AboveNormal, High); пусто — не трогать
$NoMainFullscreen = $true # не держать окно мейна на весь экран (F11)
$BalloonRejoinCount = 0  # перезаход ВСЕХ окон сразу после стольких продлений Balloon Blessing МЕЙНА (их пишет лог данных Atlas
                         # javad1khive_data_<UserId>_<день>.tsv, строки "BUF ... ev=r n=Balloon Blessing"); 0 — не следить
$BalloonQuietMinutes = 120 # до планового перезапуска меньше стольких минут — по Balloon Blessing не перезаходим (Update-BalloonRejoin)
$HelperMemCapMB  = 0     # потолок RAM (рабочего набора) окна хелпера, МБ (см. RejoinMemory); 0 — не ставить
$MemFaultLimit   = 3000  # окно у потолка подгружает страницы чаще стольких раз в сек дольше минуты — потолок поднимается
$HelperQuality   = 0     # графика хелперов (SavedQualityLevel 1–10, ставится при запуске окна); 0 — не трогать

$ErrorActionPreference = 'Stop'
if (-not $DataDir) { $DataDir = Join-Path $env:LOCALAPPDATA 'AutoRejoin' }
if (-not (Test-Path -LiteralPath $DataDir)) { $null = New-Item -ItemType Directory -Force -Path $DataDir }
$PlayerProcess = 'RobloxPlayerBeta'
$RobloxDir = Join-Path $env:LOCALAPPDATA 'Roblox'          # там GlobalBasicSettings_*.xml (FPS, полный экран, графика)
$LogDir = Join-Path $env:LOCALAPPDATA 'Roblox\logs'
$OwnLog = Join-Path $DataDir 'rejoin.log'
$ConfigFile = Join-Path $DataDir 'config.json'
$VaultFile = Join-Path $DataDir 'vault.dat'
$Workspace = Join-Path $env:LOCALAPPDATA 'Potassium\workspace'   # у REAL — Real\workspace (Set-EngineConfig)
$RealDir = Join-Path $env:LOCALAPPDATA 'Real'                       # REAL ставится сюда (Update.exe, autoexec, workspace)
$script:ExecDir = $RealDir        # папка внешнего экзекьютора (REAL, Volt, Synapse Z): там autoexec и workspace
$script:ExecExe = $null           # его exe (у REAL — Update.exe)
$script:ExecName = 'REAL'         # имя для журнала
$script:ExecProcs = @('Real')     # имена процессов (можно с *)
$PotassiumPathFile = Join-Path $DataDir 'potassium.path'   # где Потассиум был в прошлый раз
$LinkCacheFile = Join-Path $DataDir 'links.cache.json'     # share-ссылка -> placeId + linkCode (Роблокс часто отвечает 500)
$BtidFile = Join-Path $DataDir 'btid.txt'                  # browserTrackerId этого компьютера (один на все окна)
# Свои куки клиента Роблокса (в том числе RBXEventTrackerV2 с номером браузера) — см. Clear-ClientCookies.
$ClientCookieFile = Join-Path $env:LOCALAPPDATA 'Roblox\LocalStorage\RobloxCookies.dat'
$RebootStampFile = Join-Path $DataDir 'autoreboot.last'    # когда была последняя автоперезагрузка
$ScheduleStampFile = Join-Path $DataDir 'schedule.last'    # какой плановый перезапуск (момент, UTC) уже выполнен
$QualityFile = Join-Path $DataDir 'quality.json'           # графика мейна, пока хелперам ставится своя (Set-LaunchQuality)
$PidFile = Join-Path $DataDir 'engine.pid'                 # PID движка: приложение закрывает "осиротевший" движок
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Security
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

if (-not ('RejoinWindows' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class RejoinWindows {
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    // Окно занимает весь монитор — полный экран Роблокса (F11).
    public static bool IsFullscreen(IntPtr hWnd) {
        RECT window;
        if (!GetWindowRect(hWnd, out window)) return false;
        var info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(MonitorFromWindow(hWnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info)) return false;
        return window.Left <= info.rcMonitor.Left && window.Top <= info.rcMonitor.Top &&
               window.Right >= info.rcMonitor.Right && window.Bottom >= info.rcMonitor.Bottom;
    }

    // Выводит окно вперёд и жмёт в нём F11. Нажатие Alt перед этим снимает запрет Windows менять активное окно
    // из фоновой программы. Зависшее окно не трогаем; не удалось вывести вперёд — F11 не жмём (ушло бы не туда).
    public static bool PressF11(IntPtr hWnd) {
        if (IsHungAppWindow(hWnd)) return false;
        keybd_event(0x12 /* VK_MENU */, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(300);
        if (GetForegroundWindow() != hWnd) return false;
        keybd_event(0x7A /* VK_F11 */, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(50);
        keybd_event(0x7A, 0, 2, UIntPtr.Zero);
        return true;
    }

    // Значки в трее от закрытых принудительно процессов Windows убирает, только когда над ними проводят мышью.
    // "Проводим" сообщениями WM_MOUSEMOVE по каждому значку в трее и под стрелочкой — настоящий курсор не двигается.
    public static void ClearGhostTrayIcons() {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
        SweepToolbar(FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null));
        SweepToolbar(FindWindowEx(FindWindow("NotifyIconOverflowWindow", null), IntPtr.Zero, "ToolbarWindow32", null));
    }

    static void SweepToolbar(IntPtr toolbar) {
        if (toolbar == IntPtr.Zero) return;
        IntPtr sizeResult;
        if (SendMessageTimeout(toolbar, 0x043A /* TB_GETBUTTONSIZE */, IntPtr.Zero, IntPtr.Zero, 0x0002, 200, out sizeResult) == IntPtr.Zero) return;
        int size = (int)sizeResult;
        int width = Math.Max(8, size & 0xFFFF), height = Math.Max(8, (size >> 16) & 0xFFFF);
        RECT rect;
        GetClientRect(toolbar, out rect);
        IntPtr result;
        for (int y = height / 2; y < Math.Max(rect.Bottom, height); y += height)
            for (int x = width / 2; x < Math.Max(rect.Right, width); x += width)
                if (SendMessageTimeout(toolbar, 0x0200 /* WM_MOUSEMOVE */, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xFFFF)),
                        0x0002 /* SMTO_ABORTIFHUNG */, 200, out result) == IntPtr.Zero) return;   // проводник занят — в другой раз
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    // Окно процесса с таким классом — даже свёрнутое или скрытое.
    public static IntPtr FindClassWindow(int processId, string className) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lParam) => {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if ((int)pid == processId) {
                var name = new StringBuilder(256);
                GetClassName(hWnd, name, name.Capacity);
                if (name.ToString() == className) { found = hWnd; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Видимые и не свёрнутые главные окна процесса (с заголовком, без окна-владельца).
    public static IntPtr[] OpenWindows(int processId) {
        var result = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if ((int)pid == processId && IsWindowVisible(hWnd) && !IsIconic(hWnd) && GetWindow(hWnd, 4 /* GW_OWNER */) == IntPtr.Zero) {
                var text = new StringBuilder(256);
                GetWindowText(hWnd, text, text.Capacity);
                if (text.Length > 0) result.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hWnd);

    // Уменьшает окно до width x height и сворачивает, не забирая фокус у того, что сейчас открыто.
    // Только асинхронно: ShowWindow/SetWindowPos ждут ответа от окна, и 11.10 зависшее окно Роблокса так
    // остановило весь AutoRejoin (он перестал следить за окнами). Зависшее окно не трогаем совсем.
    public static void ShrinkAndMinimize(IntPtr hWnd, int width, int height) {
        if (IsHungAppWindow(hWnd)) return;
        ShowWindowAsync(hWnd, 4 /* SW_SHOWNOACTIVATE: снять "развёрнуто на весь экран" */);
        System.Threading.Thread.Sleep(150);
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, width, height, 0x0002 | 0x0004 | 0x0010 | 0x4000 /* NOMOVE | NOZORDER | NOACTIVATE | ASYNCWINDOWPOS */);
        System.Threading.Thread.Sleep(150);
        ShowWindowAsync(hWnd, 7 /* SW_SHOWMINNOACTIVE */);
    }

    // Свёрнутые окна процесса — обратно на экран, не забирая фокус (выключили "Сворачивать окна хелперов").
    // Возвращает, сколько окон вернули.
    public static int RestoreMinimized(int processId) {
        var found = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if ((int)pid == processId && IsWindowVisible(hWnd) && IsIconic(hWnd) && GetWindow(hWnd, 4 /* GW_OWNER */) == IntPtr.Zero) {
                var text = new StringBuilder(256);
                GetWindowText(hWnd, text, text.Capacity);
                if (text.Length > 0) found.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        int restored = 0;
        foreach (var hWnd in found) {
            if (IsHungAppWindow(hWnd)) continue;
            ShowWindowAsync(hWnd, 4 /* SW_SHOWNOACTIVATE */);
            restored++;
        }
        return restored;
    }

    static readonly HashSet<IntPtr> alreadyClosed = new HashSet<IntPtr>();

    // Закрывает (как крестиком) видимые окна этих процессов, в заголовке которых есть title.
    // Возвращает, сколько окон закрыто впервые.
    public static int CloseByTitle(int[] processIds, string title) {
        var ids = new HashSet<int>(processIds);
        var found = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (ids.Contains((int)pid) && IsWindowVisible(hWnd)) {
                var text = new StringBuilder(256);
                GetWindowText(hWnd, text, text.Capacity);
                if (text.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) found.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        int count = 0;
        foreach (var hWnd in found) {
            PostMessage(hWnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            if (alreadyClosed.Add(hWnd)) count++;
        }
        return count;
    }

    // PID тех из этих процессов, у которых есть видимое окно с заголовком ровно из списка titles.
    public static int[] PidsWithTitle(int[] processIds, string[] titles) {
        var ids = new HashSet<int>(processIds);
        var wanted = new HashSet<string>(titles, StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<int>();
        EnumWindows((hWnd, lParam) => {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (ids.Contains((int)pid) && IsWindowVisible(hWnd)) {
                var text = new StringBuilder(256);
                GetWindowText(hWnd, text, text.Capacity);
                if (wanted.Contains(text.ToString().Trim())) result.Add((int)pid);
            }
            return true;
        }, IntPtr.Zero);
        return new List<int>(result).ToArray();
    }
}
'@
}

# Держит приоритет ЦП окна: Роблокс сам опускает приоритет своего процесса каждые ~10 сек, а главный цикл
# проверяет окна раз в 1-2 сек — поэтому проверка идёт в фоне по таймеру каждые 100 мс.
if (-not ('RejoinPriority' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

public static class RejoinPriority {
    const uint PROCESS_SET_INFORMATION = 0x0200;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint STILL_ACTIVE = 259;
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern uint GetPriorityClass(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);

    static readonly Dictionary<int, IntPtr> handles = new Dictionary<int, IntPtr>();
    static readonly Dictionary<int, uint> wanted = new Dictionary<int, uint>();
    static Timer timer;
    public static int Restores;   // сколько раз Роблокс опускал приоритет, а мы возвращали

    public static bool Hold(int pid, uint priorityClass) {
        lock (handles) {
            IntPtr handle;
            if (!handles.TryGetValue(pid, out handle)) {
                handle = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (handle == IntPtr.Zero) return false;
                handles[pid] = handle;
            }
            wanted[pid] = priorityClass;
            if (GetPriorityClass(handle) != priorityClass) SetPriorityClass(handle, priorityClass);
            if (timer == null) timer = new Timer(Tick, null, 100, 100);
            return true;
        }
    }

    // Больше не держать приоритет окна (аккаунт перестал быть мейном или приоритет в настройках сняли).
    public static void Release(int pid) {
        lock (handles) {
            IntPtr handle;
            if (!handles.TryGetValue(pid, out handle)) return;
            CloseHandle(handle);
            handles.Remove(pid);
            wanted.Remove(pid);
        }
    }

    static void Tick(object state) {
        lock (handles) {
            foreach (int pid in new List<int>(handles.Keys)) {
                IntPtr handle = handles[pid];
                uint code;
                if (!GetExitCodeProcess(handle, out code) || code != STILL_ACTIVE) {
                    CloseHandle(handle);
                    handles.Remove(pid);
                    wanted.Remove(pid);
                    continue;
                }
                uint current = GetPriorityClass(handle);
                if (current != 0 && current != wanted[pid] && SetPriorityClass(handle, wanted[pid])) Restores++;
            }
        }
    }
}
'@
}

# Потолок RAM окна хелпера (настройка "Потолок RAM хелперов"). Не как Mem Reduct, который раз за разом выгружает
# из памяти ВСЕ процессы (и мейна), а окно отрастает обратно за 1–2 минуты (замер 07.11: 343–631 -> 985–1463 МБ):
#  - окну хелпера ставится жёсткий предел рабочего набора (SetProcessWorkingSetSizeEx, HARDWS_MAX_ENABLE) — больше
#    него в RAM окно не держит; дальше Windows сама выгружает самые давно не нужные страницы именно этого окна, горячие
#    остаются; один раз сразу выгружается всё, что осталось от загрузки (пик ~2 ГБ), и приоритет памяти — низкий:
#    при нехватке памяти Windows сперва отдаёт страницы хелперов, а не мейна и не файлового кэша;
#  - раз в 5 сек таймер смотрит, как часто окно подгружает страницы обратно (page faults/сек, сглаженно): упёрлось
#    в потолок и подгружает чаще FaultLimit дольше минуты — потолок +25% (не выше 3x настройки); 10 минут спокойно —
#    −10% (не ниже настройки). Потолок сбросили — возвращает. Окно закрылось — забывает.
# Прав администратора не нужно: окна Роблокса запущены под тем же пользователем.
if (-not ('RejoinMemory' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

public static class RejoinMemory {
    const uint PROCESS_SET_QUOTA = 0x0100;
    const uint PROCESS_SET_INFORMATION = 0x0200;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint STILL_ACTIVE = 259;
    public const uint HARDWS_MIN_DISABLE = 0x2, HARDWS_MAX_ENABLE = 0x4, HARDWS_MAX_DISABLE = 0x8;
    const int ProcessMemoryPriority = 0;                 // PROCESS_INFORMATION_CLASS
    const uint MEMORY_PRIORITY_LOW = 2, MEMORY_PRIORITY_NORMAL = 5;
    const int SE_INC_WORKING_SET_PRIVILEGE = 33;         // есть у обычного пользователя, нужна для SetProcessWorkingSetSizeEx
    const long Mb = 1L << 20;
    const long MinWorkingSet = 2 * Mb;
    const long DefaultMin = 204800, DefaultMax = 1413120; // обычные значения Windows (50 и 345 страниц)

    public const int TickMs = 5000;
    public const double Alpha = 0.2;          // сглаживание page faults: ~25 сек при тике 5 сек
    public const double GraceSec = 60;        // первую минуту окно подтягивает горячие страницы — не судим
    public const double RaiseAfterSec = 60;   // столько подряд выше FaultLimit у потолка — поднимаем
    public const double CalmAfterSec = 600;   // столько подряд ниже FaultLimit/4 — опускаем
    public const double NearCap = 0.9;        // "упёрлось в потолок": рабочий набор не меньше 90% потолка
    public const double RaiseStep = 1.25, LowerStep = 0.9, MaxFactor = 3.0;

    public sealed class Entry {
        public int Pid;
        public long BaseCap, Cap;
        public double FaultLimit, Ema;
        public int Samples, Raises;
        public bool MaxedOut;
        public DateTime HeldAt, LastAt;
        public DateTime OverSince = DateTime.MinValue, CalmSince = DateTime.MinValue;
        public uint LastFaults;
        public long WorkingSet, WsBefore, WsAfter;
        internal IntPtr Handle;
        internal UIntPtr OldMin, OldMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_MEMORY_COUNTERS {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessWorkingSetSizeEx(IntPtr handle, UIntPtr min, UIntPtr max, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessWorkingSetSizeEx(IntPtr handle, out UIntPtr min, out UIntPtr max, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessInformation(IntPtr handle, int infoClass, ref uint info, int size);
    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)] static extern bool EmptyWorkingSet(IntPtr handle);
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] static extern bool GetProcessMemoryInfo(IntPtr handle, out PROCESS_MEMORY_COUNTERS counters, int size);
    [DllImport("ntdll.dll")] static extern int RtlAdjustPrivilege(int privilege, [MarshalAs(UnmanagedType.U1)] bool enable,
        [MarshalAs(UnmanagedType.U1)] bool currentThread, [MarshalAs(UnmanagedType.U1)] out bool wasEnabled);

    static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    static readonly List<string> events = new List<string>();
    static Timer timer;
    static bool privilege;

    static long RoundMb(double bytes) { return (long)Math.Round(bytes / Mb) * Mb; }

    // Подстройка потолка по частоте page faults (rate — в сек) и рабочему набору; без процессов — чтобы её можно было
    // проверить. 1 — потолок поднят, -1 — опущен, 0 — без изменений.
    public static int Adjust(Entry e, double rate, long workingSet, DateTime now) {
        e.Ema = e.Samples == 0 ? rate : e.Ema + Alpha * (rate - e.Ema);
        e.Samples++;
        e.WorkingSet = workingSet;
        if ((now - e.HeldAt).TotalSeconds < GraceSec) return 0;
        long maxCap = RoundMb(e.BaseCap * MaxFactor);
        // Подгружает часто, но до потолка далеко — дело не в потолке (его выгрузил кто-то другой или он просто много
        // выделяет): потолок тут не поможет.
        if (e.Ema > e.FaultLimit && workingSet >= e.Cap * NearCap) {
            e.CalmSince = DateTime.MinValue;
            if (e.OverSince == DateTime.MinValue) { e.OverSince = now; return 0; }
            if ((now - e.OverSince).TotalSeconds < RaiseAfterSec || e.Cap >= maxCap) return 0;
            e.Cap = Math.Min(RoundMb(e.Cap * RaiseStep), maxCap);
            e.OverSince = now;
            e.Raises++;
            if (e.Cap >= maxCap) e.MaxedOut = true;
            return 1;
        }
        e.OverSince = DateTime.MinValue;
        if (e.Ema >= e.FaultLimit / 4 || e.Cap <= e.BaseCap) { e.CalmSince = DateTime.MinValue; return 0; }
        if (e.CalmSince == DateTime.MinValue) { e.CalmSince = now; return 0; }
        if ((now - e.CalmSince).TotalSeconds < CalmAfterSec) return 0;
        e.Cap = Math.Max(RoundMb(e.Cap * LowerStep), e.BaseCap);
        e.CalmSince = now;
        e.MaxedOut = false;
        return -1;
    }

    static bool Counters(IntPtr handle, out PROCESS_MEMORY_COUNTERS c) {
        c = new PROCESS_MEMORY_COUNTERS();
        c.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS));
        return GetProcessMemoryInfo(handle, out c, (int)c.cb);
    }

    static bool Apply(Entry e) {
        long min = Math.Min(MinWorkingSet, e.Cap / 2);
        return SetProcessWorkingSetSizeEx(e.Handle, new UIntPtr((ulong)min), new UIntPtr((ulong)e.Cap), HARDWS_MAX_ENABLE | HARDWS_MIN_DISABLE);
    }

    static void SetPriority(IntPtr handle, uint priority) {
        SetProcessInformation(handle, ProcessMemoryPriority, ref priority, sizeof(uint));
    }

    static void Event(Entry e, string text) {
        if (events.Count < 200) events.Add(e.Pid + "|" + text);
    }

    // Поставить потолок окну. "ok" — поставлен (или уже стоит), иначе — что не так.
    public static string Hold(int pid, long capBytes, double faultLimit) {
        lock (entries) {
            if (!privilege) {
                bool was;
                RtlAdjustPrivilege(SE_INC_WORKING_SET_PRIVILEGE, true, false, out was);
                privilege = true;
            }
            if (entries.ContainsKey(pid)) return "ok";
            IntPtr handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return "нет доступа к процессу (ошибка " + Marshal.GetLastWin32Error() + ")";
            Entry e = new Entry();
            e.Pid = pid; e.Handle = handle; e.BaseCap = e.Cap = capBytes; e.FaultLimit = faultLimit;
            e.HeldAt = e.LastAt = DateTime.UtcNow;
            uint oldFlags;
            if (!GetProcessWorkingSetSizeEx(handle, out e.OldMin, out e.OldMax, out oldFlags) || e.OldMax == UIntPtr.Zero) {
                e.OldMin = new UIntPtr((ulong)DefaultMin); e.OldMax = new UIntPtr((ulong)DefaultMax);
            }
            PROCESS_MEMORY_COUNTERS c;
            if (Counters(handle, out c)) e.WsBefore = (long)c.WorkingSetSize.ToUInt64();
            SetPriority(handle, MEMORY_PRIORITY_LOW);   // не вышло — не страшно, главное потолок
            EmptyWorkingSet(handle);
            if (!Apply(e)) {
                int error = Marshal.GetLastWin32Error();
                SetPriority(handle, MEMORY_PRIORITY_NORMAL);
                CloseHandle(handle);
                return "Windows не дала поставить потолок (ошибка " + error + ")";
            }
            if (Counters(handle, out c)) { e.WsAfter = e.WorkingSet = (long)c.WorkingSetSize.ToUInt64(); e.LastFaults = c.PageFaultCount; }
            entries[pid] = e;
            if (timer == null) timer = new Timer(Tick, null, TickMs, TickMs);
            return "ok";
        }
    }

    // Снять потолок: обычные границы Windows без жёсткого предела, обычный приоритет памяти.
    public static void Release(int pid) {
        lock (entries) {
            Entry e;
            if (!entries.TryGetValue(pid, out e)) return;
            Unhold(e);
            entries.Remove(pid);
        }
    }

    public static void ReleaseAll() {
        lock (entries) {
            foreach (Entry e in entries.Values) Unhold(e);
            entries.Clear();
        }
    }

    static void Unhold(Entry e) {
        SetProcessWorkingSetSizeEx(e.Handle, e.OldMin, e.OldMax, HARDWS_MAX_DISABLE | HARDWS_MIN_DISABLE);
        SetPriority(e.Handle, MEMORY_PRIORITY_NORMAL);
        CloseHandle(e.Handle);
    }

    // Настройку поменяли на ходу: новый потолок всем окнам (подстройка начинается заново).
    public static void SetLimits(long capBytes, double faultLimit) {
        lock (entries) {
            foreach (Entry e in entries.Values) {
                e.FaultLimit = faultLimit;
                if (e.BaseCap == capBytes) continue;
                e.BaseCap = e.Cap = capBytes;
                e.Raises = 0; e.MaxedOut = false;
                e.OverSince = e.CalmSince = DateTime.MinValue;
                Apply(e);
            }
        }
    }

    public static Entry Find(int pid) {
        lock (entries) { Entry e; return entries.TryGetValue(pid, out e) ? e : null; }
    }

    public static long CapOf(int pid) {
        Entry e = Find(pid);
        return e == null ? 0 : e.Cap;
    }

    // Границы рабочего набора процесса как их видит Windows: min, max, флаги (для проверок).
    public static long[] Limits(int pid) {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return new long[] { 0, 0, 0 };
        try {
            UIntPtr min, max; uint flags;
            if (!GetProcessWorkingSetSizeEx(handle, out min, out max, out flags)) return new long[] { 0, 0, 0 };
            return new long[] { (long)min.ToUInt64(), (long)max.ToUInt64(), flags };
        } finally { CloseHandle(handle); }
    }

    // Строки для журнала: "pid|текст".
    public static string[] DrainEvents() {
        lock (entries) {
            string[] list = events.ToArray();
            events.Clear();
            return list;
        }
    }

    public static void TickNow() { Tick(null); }

    static void Tick(object state) {
        lock (entries) {
            DateTime now = DateTime.UtcNow;
            foreach (int pid in new List<int>(entries.Keys)) {
                Entry e = entries[pid];
                uint code;
                if (!GetExitCodeProcess(e.Handle, out code) || code != STILL_ACTIVE) {
                    CloseHandle(e.Handle);
                    entries.Remove(pid);
                    continue;
                }
                PROCESS_MEMORY_COUNTERS c;
                if (!Counters(e.Handle, out c)) continue;
                double seconds = (now - e.LastAt).TotalSeconds;
                double rate = seconds > 0.5 ? unchecked(c.PageFaultCount - e.LastFaults) / seconds : e.Ema;
                e.LastFaults = c.PageFaultCount;
                e.LastAt = now;
                int change = Adjust(e, rate, (long)c.WorkingSetSize.ToUInt64(), now);
                if (change > 0) {
                    Event(e, string.Format("потолок RAM поднят до {0} МБ: окно упирается в него и часто подгружает выгруженные страницы ({1:N0} в сек){2}",
                        e.Cap / Mb, e.Ema, e.MaxedOut ? " — это максимум (3x настройки)" : ""));
                } else if (change < 0) {
                    Event(e, string.Format("потолок RAM снова {0} МБ: подгрузка утихла", e.Cap / Mb));
                }
                UIntPtr min, max; uint flags;
                bool lost = GetProcessWorkingSetSizeEx(e.Handle, out min, out max, out flags) &&
                    ((flags & HARDWS_MAX_ENABLE) == 0 || Math.Abs((long)max.ToUInt64() - e.Cap) > Mb);
                if ((change != 0 || lost) && Apply(e) && lost && change == 0) {
                    Event(e, string.Format("потолок RAM снова на месте ({0} МБ): его сбросили", e.Cap / Mb));
                }
            }
            if (entries.Count == 0 && timer != null) { timer.Dispose(); timer = null; }
        }
    }
}
'@
}

# Команды от приложения: строки из stdin читает фоновый поток, главный цикл забирает их по очереди (Invoke-AppCommands).
# Signal будит паузу главного цикла (Wait-Loop), чтобы команда выполнялась сразу, а не через секунду.
if (-not ('RejoinInput' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

public static class RejoinInput {
    static readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
    static Thread reader;
    public static readonly AutoResetEvent Signal = new AutoResetEvent(false);
    public static volatile bool Closed;   // stdin закрылся: приложение закрыто или упало

    public static void Start() {
        if (reader != null) return;
        reader = new Thread(() => {
            try {
                var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                string line;
                while ((line = input.ReadLine()) != null) {
                    if (line.Trim().Length == 0) continue;
                    queue.Enqueue(line.Trim());
                    Signal.Set();
                }
            } catch { }
            Closed = true;
            Signal.Set();
        });
        reader.IsBackground = true;
        reader.Start();
    }

    // Для проверок: команда, как будто пришла от приложения.
    public static void Push(string line) { queue.Enqueue(line); Signal.Set(); }

    public static string Next() {
        string line;
        return queue.TryDequeue(out line) ? line : null;
    }
}
'@
}

# Строки из логов Роблокса: заход на сервер, ID аккаунта и вылет с сервера.
$JoinPattern       = "! Joining game '[^']+' place (\d+)"
$UserIdPattern     = 'game_join_loadtime:.*\buserid:(\d+)'
$DisconnectPattern = 'Disconnected from server for reason:\s*(?<r>.+)$|Sending disconnect with reason:\s*(?<r>\d+)|Time to disconnect replication data|Lost connection|Connection lost|Disconnection Notification|ID_CONNECTION_LOST|ID_DISCONNECTION_NOTIFICATION|leaveUGCGameInternal'
$KickPattern       = 'setting replicator disconnect reason to (\d+)'
# Роблокс не принял вход в окне: билет (gameinfo) не прошёл, окно закрывается, не зайдя в игру. Вторая строка —
# причина, из-за которой это случается чаще всего (см. Get-BrowserTrackerId).
$WebLoginPattern   = 'WebLogin HTTP error, statusCode:\s*(\d+)'
$BtidMismatchPattern = 'Cookie BTID is not the same as the web BTID passed in'

# Коды вылета Роблокса (из строки "setting replicator disconnect reason to N").
function Get-KickText([int]$Code) {
    $text = switch ($Code) {
        256 { 'потеряна связь с сервером: неизвестная ошибка или сервер закрылся' }
        264 { 'аккаунт зашёл в эту игру с другого устройства' }
        266 { 'сервер не отвечает (таймаут)' }
        267 { 'кикнули из игры' }
        268 { 'Роблокс заподозрил стороннюю программу (Unexpected client behavior)' }
        273 { 'аккаунт зашёл в игру с другого устройства' }
        274 { 'сервер закрылся' }
        275 { 'сервер закрыт на обслуживание' }
        277 { 'потеряна связь с сервером' }
        278 { 'кик за 20 минут бездействия' }
        279 { 'не удалось подключиться к серверу' }
        280 { 'клиент Роблокса устарел' }
        282 { 'потеряна связь с сервером' }
        284 { 'сервер не отвечает' }
        286 { 'сервер выключили' }
        288 { 'сервер закрылся' }
        292 { 'игра перегружена (лаги сервера)' }
        default { 'вылет с сервера' }
    }
    return "$text (код $Code)"
}

# Заголовки окон, которые Роблокс показывает, когда падает. Пока такое окно висит, процесс жив и "отвечает",
# но игры в нём уже нет.
$CrashTitles = @('Roblox Crash', 'Error', 'Memory dump captured')

$script:Accounts = @()
$script:Trackers = @{}   # PID окна Роблокса -> что о нём известно из лога
$script:KilledPids = @{}  # PID окна, закрытого для перезахода -> время старта того процесса (пока он не исчез)
$script:LastLaunchAt = [datetime]::MinValue
$script:LastMemoryRestart = [datetime]::MinValue
$script:FreeMemoryAt = [datetime]::MinValue
$script:FreeMemoryGB = $null
$script:LastPotassiumRestart = [datetime]::MinValue
$script:PotassiumExe = $null   # путь к Потассиуму, подсмотренный у запущенного процесса
$script:PotassiumPid = $null   # к какому запуску Потассиума относятся строки "Client <PID> attached"
$script:AttachedPids = @{}     # PID -> когда Потассиум последний раз написал "Client <PID> attached"
$script:PotassiumUiMissingSince = $null   # с какого момента окно Потассиума пустое (интерфейс упал, процесс живой)
$script:PotassiumUiWarned = $false
$script:PotassiumCheckAt = [datetime]::MinValue   # когда последний раз читали окно Потассиума
$script:PotassiumSignedOutSince = $null   # с какого момента Потассиум стоит на экране входа (разлогинился)
$script:PotassiumSignInWarnedAt = [datetime]::MinValue
$script:PotassiumSignInTries = 0          # попыток автовхода с тех пор, как Потассиум показал экран входа
$script:PotassiumSignInAt = [datetime]::MinValue
$script:PotassiumSignInAlerted = $false
$script:LastReadyAt = [datetime]::MinValue        # когда в последнем окне запустился скрипт (пауза перед следующим запуском)
$script:MainPendingSince = $null                  # с какого момента мейн не в игре со скриптом (остальные окна ждут)
$script:PotassiumStartedAt = $null                # движок только что открыл Потассиум (ещё идут его $PotassiumWarmup сек)
$script:TrayWarned = $false
$script:InstantDeaths = 0      # окон подряд, умерших в первые секунды без лога
$script:LaunchBlocked = $false # новые запуски бесполезны до перезагрузки Windows
$script:Deferred = @()         # аккаунты, которые Роблокс сейчас не пускает (403) или не удалось проверить: перепроверяем позже
$script:AuthAsked = @{}        # ключ аккаунта -> когда и с какой кукой попросили приложение взять новую (401/403)
$script:LinkCache = $null      # кэш share-ссылок (читается из links.cache.json при первом обращении)
$script:FpsHoldUntil = $null   # до какого момента удерживать FramerateCap только что запущенного окна
$script:FpsHoldValue = 0
$script:FpsHoldFullscreen = $false
$script:FpsHoldQuality = -1
$script:QualityState = $null   # quality.json в памяти (Get-QualityState)
$script:QualityLoaded = $false
$script:TrayDirtyAt = $null    # когда закрылось окно Роблокса — значит, в трее мог остаться мёртвый значок
$script:WorkspaceScanAt = [datetime]::MinValue   # файлы скрипта Hive в workspace Потассиума (запуск, пульс, ошибки)
$script:WorkspaceFiles = @{}   # имя файла -> время последней записи
$script:AutoAttachCheckAt = [datetime]::MinValue   # когда последний раз проверяли, что автоаттач Потассиума включён
$script:AutoAttachWarned = $false
$script:AutoAttachFails = 0
$script:ManualAttachAt = [datetime]::MinValue   # когда движок последний раз сам жал Attach в Потассиуме (Invoke-ManualAttach)
$script:PotassiumButtons = ''  # кнопки в окне Потассиума, если Attach среди них не нашёлся (для журнала)
$script:FixFails = @()         # когда "Fix it" в Потассиуме не помог (Register-FixFailure)
$script:FixSuspendedUntil = $null   # 3 неудачи "Fix it" за час: до этого момента не чиним и Attach сами не жмём
$script:UsedLogs = @{}         # лог окна Роблокса -> когда его окно закрылось: этот лог другому окну не выдаём (Find-TrackerLog)
$script:RebootRetryAt = $null  # новые окна умирают, а автоперезагрузка была недавно — когда попробовать снова
$script:WerScanAt = [datetime]::MinValue   # когда последний раз искали окна Windows "Прекращена работа программы" от Роблокса
$script:TraySweptAt = [datetime]::MinValue
$script:DefaultLink = $null    # текущая ссылка по умолчанию (из $LinkFile или $Link)
$script:DefaultLinkCheckedAt = [datetime]::MinValue

# Приложение
$script:Out = $null            # stdout для приложения (строки JSON); $null — приложения нет или оно закрылось
$script:LogDay = $null         # день последней строки в rejoin.log (при смене дня пишем разделитель с датой)
$script:StopRequested = $false # приложение попросило остановиться
$script:Phase = 'starting'     # что движок сейчас делает: starting, delay, network, accounts, potassium, running
$script:Problems = @{}         # ключ аккаунта -> почему он не в работе (Text, Cookie — с какой кукой не вышло)
$script:ConfigStamp = $null    # время записи и размер config.json / vault.dat, которые уже применены
$script:VaultStamp = $null
$script:ConfigCheckAt = [datetime]::MinValue
$script:VaultPotassium = $null # логин и пароль Потассиума из vault.dat (в лог не пишутся)
$script:Schedule = [pscustomobject]@{ Enabled = $false; Time = '09:51'; Zone = ''; Action = 'rejoin' }
$script:ScheduleArmedAt = [datetime]::UtcNow   # с какого момента действует текущее расписание (раньше — не срабатывает)
$script:ScheduleLast = $null   # последний выполненный плановый перезапуск (момент по расписанию, UTC)
$script:ScheduleNextUtc = $null
$script:RebootAt = $null       # когда Windows перезагрузится (перезагрузка запрошена движком и не отменена)
$script:StatusAt = [datetime]::MinValue
$script:PotassiumClients = $null   # сколько клиентов показывает Потассиум (последний раз, когда читали его окно)
$script:TotalMemoryGB = $null
$script:GuiGoneLogged = $false
$script:StatusErrorLogged = $false   # ошибка сборки состояния для приложения уже записана в журнал

# Строка JSON для приложения. Приложение закрылось (stdin/stdout оборваны) — дальше работаем молча, только в лог-файл.
function Send-App($Object) {
    if (-not $script:Out) { return }
    try { $script:Out.WriteLine(($Object | ConvertTo-Json -Compress -Depth 6)) }
    catch { $script:Out = $null }
}

function Write-Status([string]$Message, [string]$Color = 'Gray', $Account) {
    $now = Get-Date
    $name = if ($Account) { [string]$Account.Name } else { '' }
    $text = if ($Account) { "[$name] $Message" } else { $Message }
    $day = $now.ToString('dd.MM.yyyy')
    try {
        if ($script:LogDay -ne $day) {
            $script:LogDay = $day
            Add-Content -LiteralPath $OwnLog -Value "===== $day =====" -Encoding UTF8
        }
        Add-Content -LiteralPath $OwnLog -Value ('[{0:HH:mm:ss}] {1}' -f $now, $text) -Encoding UTF8
    } catch {}
    Send-App ([ordered]@{
        t = 'log'; time = $now.ToString('HH:mm:ss'); date = $now.ToString('yyyy-MM-dd'); color = $Color
        acc = $name; key = if ($Account) { [string]$Account.Key } else { '' }; msg = $Message
    })
}

# То, что без тебя не починить: приложение показывает это плашкой и уведомлением в трее. Приложения нет — окно msg.exe
# (висит до 24 ч, движок его не ждёт).
function Show-Alert([string]$Message) {
    Write-Status $Message Red
    if ($script:Out) { Send-App ([ordered]@{ t = 'alert'; msg = $Message }); if ($script:Out) { return } }
    try { Start-Process -FilePath 'msg.exe' -ArgumentList @('*', '/TIME:86400', "AutoRejoin: $Message") -WindowStyle Hidden } catch {}
}

#region Ссылки и запуск

# Разбирает ссылку на игру / приватный сервер. В сеть не ходит.
function Get-LaunchTarget([string]$Url) {
    $Url = $Url.Trim()
    $query = @{}
    $parts = ($Url -split '#', 2)[0] -split '\?', 2
    if ($parts.Count -eq 2) {
        foreach ($pair in $parts[1] -split '&') {
            $key, $value = $pair -split '=', 2
            if ($key) { $query[$key] = [uri]::UnescapeDataString([string]$value) }
        }
    }

    $target = [pscustomobject]@{ PlaceId = $null; LinkCode = $null; JobId = $null; ShareCode = $null }
    if ($Url -match '^\d+$') { $target.PlaceId = $Url }
    elseif ($Url -match '/share\b' -and $query.code) {
        if ($query.type -and $query.type -ne 'Server') { throw "Share-ссылки типа '$($query.type)' не поддерживаются, нужна ссылка на сервер: $Url" }
        $target.ShareCode = $query.code
    }
    elseif ($Url -match '/games/(\d+)') { $target.PlaceId = $Matches[1] }
    elseif ($query.placeId -match '^\d+$') { $target.PlaceId = $query.placeId }
    else { throw "Не понимаю ссылку: $Url" }

    $target.LinkCode = if ($query.privateServerLinkCode) { $query.privateServerLinkCode } else { $query.linkCode }
    $target.JobId    = if ($query.gameInstanceId) { $query.gameInstanceId } else { $query.jobId }
    return $target
}

# browserTrackerId — номер "браузера" (кука RBXEventTrackerV2), от имени которого сайт нажимает "Играть". Клиент
# сверяет его с номером в своей собственной куке RBXEventTrackerV2 и, если номера разные, пишет в лог
# "Cookie BTID is not the same as the web BTID passed in", получает 403 на authentication-ticket/redeem
# ("WebLogin HTTP error, statusCode: 403") и закрывается, так и не зайдя в игру. Новая кука аккаунта от этого не
# помогает: дело не в сессии, а в номере браузера. 29.10 мейн из-за этого не заходил с 09:51 до 11:28 (97 мин: билет
# выдавался, окно открывалось, Потассиум подключался — и окно уходило в "не зашёл на сервер за 60 сек").
# Поэтому номер у нас один на компьютер (как у настоящего браузера), а не случайный на каждый запуск.
function Get-BrowserTrackerId {
    if ($script:BrowserTrackerId) { return $script:BrowserTrackerId }
    $saved = try { (Get-Content -LiteralPath $BtidFile -Raw -ErrorAction Stop).Trim() } catch { '' }
    if ($saved -match '^\d{16}$') { $script:BrowserTrackerId = $saved; return $saved }
    $script:BrowserTrackerId = '1{0:D7}{1:D8}' -f (Get-Random -Maximum 10000000), (Get-Random -Maximum 100000000)
    try { Set-Content -LiteralPath $BtidFile -Value $script:BrowserTrackerId -Encoding UTF8 } catch {}
    return $script:BrowserTrackerId
}

# Свою куку RBXEventTrackerV2 клиент сохраняет в LocalStorage\RobloxCookies.dat и берёт оттуда при следующем запуске,
# а номер в ней Роблокс выдаёт свой (наш "suggestedBrowserTrackerId" он принимает не всегда) — тогда номера
# расходятся и вход в окне срывается (см. Get-BrowserTrackerId). Файл общий для всех окон и нужен клиенту только
# чтобы помнить вход, а мы и так входим по билету на каждый запуск, — поэтому перед запуском окна его убираем.
# Заодно в окно не попадает чужая сессия, оставшаяся в этом файле от окна другого аккаунта.
function Clear-ClientCookies {
    try {
        if (Test-Path -LiteralPath $ClientCookieFile) { Remove-Item -LiteralPath $ClientCookieFile -Force }
    } catch {
        Write-Status "не получилось убрать куки клиента ($(Split-Path $ClientCookieFile -Leaf)): $($_.Exception.Message)" DarkYellow
    }
}

# Такая же ссылка roblox-player:, какую открывает сайт по кнопке "Играть".
function New-LaunchUri([string]$Ticket, $Target) {
    $browserTracker = Get-BrowserTrackerId
    $joinAttempt = [guid]::NewGuid()
    $query = if ($Target.LinkCode) {
        "request=RequestPrivateGame&browserTrackerId=$browserTracker&placeId=$($Target.PlaceId)&linkCode=$([uri]::EscapeDataString($Target.LinkCode))&joinAttemptId=$joinAttempt&joinAttemptOrigin=PlayButton"
    } elseif ($Target.JobId) {
        "request=RequestGameJob&browserTrackerId=$browserTracker&placeId=$($Target.PlaceId)&gameId=$([uri]::EscapeDataString($Target.JobId))&isPlayTogetherGame=false&joinAttemptId=$joinAttempt&joinAttemptOrigin=PlayButton"
    } else {
        "request=RequestGame&browserTrackerId=$browserTracker&placeId=$($Target.PlaceId)&isPlayTogetherGame=false&referredByPlayerId=0&joinAttemptId=$joinAttempt&joinAttemptOrigin=PlayButton"
    }
    $placeLauncher = [uri]::EscapeDataString("https://www.roblox.com/Game/PlaceLauncher.ashx?$query")
    $launchTime = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    return "roblox-player:1+launchmode:play+gameinfo:$Ticket+launchtime:$launchTime+placelauncherurl:$placeLauncher+browsertrackerid:$browserTracker+robloxLocale:en_us+gameLocale:en_us+LaunchExp:InApp"
}

#endregion

#region API Роблокса

# Запрос к API от имени аккаунта. Сам получает CSRF-токен (первый POST всегда отвечает 403 с токеном).
function Invoke-RobloxApi($Account, [string]$Method, [string]$Uri, [string]$Body = '') {
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $request = [Net.HttpWebRequest]::Create($Uri)
        $request.Method = $Method
        $request.Timeout = 15000
        $request.UserAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36'
        $request.Referer = 'https://www.roblox.com/'
        $request.Headers['Cookie'] = ".ROBLOSECURITY=$($Account.Cookie)"
        $request.Headers['RBXAuthenticationNegotiation'] = '1'
        if ($Account.Csrf) { $request.Headers['X-CSRF-TOKEN'] = $Account.Csrf }
        if ($Method -ne 'GET') {
            $bytes = [Text.Encoding]::UTF8.GetBytes($Body)
            $request.ContentType = 'application/json'
            $request.ContentLength = $bytes.Length
            $stream = $request.GetRequestStream()
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Dispose()
        }

        try {
            $response = $request.GetResponse()
        } catch {
            $webError = $_.Exception
            while ($webError -and $webError -isnot [Net.WebException]) { $webError = $webError.InnerException }
            if (-not $webError -or -not $webError.Response) { throw }
            $status = [int]$webError.Response.StatusCode
            $csrf = $webError.Response.Headers['x-csrf-token']
            $errorBody = ''
            try { $errorBody = (New-Object IO.StreamReader($webError.Response.GetResponseStream())).ReadToEnd() } catch {}
            $webError.Response.Dispose()
            if ($status -eq 403 -and $csrf -and $attempt -eq 1) { $Account.Csrf = $csrf; continue }
            if ($status -eq 401) { throw 'кука недействительна или устарела (401)' }
            # 26.10: так Роблокс отвечает на всё (и на билет на вход), пока на аккаунте не принятое предупреждение или бан.
            if ($status -eq 403 -and $errorBody -match 'moderated') { throw 'Роблокс не пускает аккаунт: на нём предупреждение или бан (403, User is moderated)' }
            throw "Роблокс ответил ошибкой $status ($Uri)"
        }

        try {
            $headers = @{}
            foreach ($key in $response.Headers.AllKeys) { $headers[$key] = $response.Headers[$key] }
            $reader = New-Object IO.StreamReader($response.GetResponseStream())
            return [pscustomobject]@{ Headers = $headers; Content = $reader.ReadToEnd() }
        } finally { $response.Dispose() }
    }
    throw "Роблокс не принял CSRF-токен ($Uri)"
}

function Get-AuthTicket($Account) {
    $ticket = (Invoke-RobloxApi $Account POST 'https://auth.roblox.com/v1/authentication-ticket').Headers['rbx-authentication-ticket']
    if (-not $ticket) { throw 'Роблокс не выдал билет на вход' }
    return $ticket
}

# Новые ссылки вида /share?code=...&type=Server превращаем в placeId + linkCode (код самого приватного сервера).
# Share-ссылки истекают (10.10 Роблокс отвечал по ссылке status=Expired, 09.10 из-за истёкшей ссылки окна не
# запускались с 10:23 до 10:31), а код сервера остаётся рабочим, пока владелец его не сменит. Поэтому код сохраняем
# в links.cache.json и дальше Роблокс не спрашиваем: заодно пропадают его ошибки 500 на этот запрос, из-за которых
# каждый запуск окна откладывался на 4–5 сек. Ссылка истекла, а кода ещё нет — берём его из уже открытых окон.
function Get-LinkCache {
    if ($null -eq $script:LinkCache) {
        $script:LinkCache = @{}
        try {
            $saved = Get-Content -LiteralPath $LinkCacheFile -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json
            foreach ($entry in $saved.PSObject.Properties) { $script:LinkCache[$entry.Name] = $entry.Value }
        } catch {}
    }
    return $script:LinkCache
}

function Save-LinkCache {
    try { [pscustomobject]$script:LinkCache | ConvertTo-Json | Set-Content -LiteralPath $LinkCacheFile -Encoding UTF8 } catch {}
}

# Код приватного сервера из командной строки уже открытых окон Роблокса (он там в placelauncherurl). Берём, только
# если у всех окон он один и тот же — иначе непонятно, какой из них от этой ссылки.
function Find-RunningLinkCode {
    $found = @{}
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='$PlayerProcess.exe'" -ErrorAction SilentlyContinue)) {
        if ($process.CommandLine -notmatch 'placelauncherurl:([^+\s"]+)') { continue }
        $url = [uri]::UnescapeDataString($Matches[1])
        if ($url -match 'linkCode=([^&]+)' ) { $linkCode = $Matches[1] } else { continue }
        $placeId = if ($url -match 'placeId=(\d+)') { $Matches[1] } else { '' }
        $found["$placeId|$linkCode"] = [pscustomobject]@{ PlaceId = $placeId; LinkCode = $linkCode }
    }
    if ($found.Count -eq 1) { return @($found.Values)[0] }
    return $null
}

function Resolve-ShareLink($Account) {
    $code = $Account.Target.ShareCode
    $cache = Get-LinkCache
    $cached = $cache[$code]

    if (-not $cached -or $cached.Stale) {
        $body = @{ linkId = $code; linkType = 'Server' } | ConvertTo-Json -Compress
        $invite = $null; $problem = $null
        for ($try = 1; $try -le 3 -and -not $invite; $try++) {
            try {
                $answer = (Invoke-RobloxApi $Account POST 'https://apis.roblox.com/sharelinks/v1/resolve-link' $body).Content | ConvertFrom-Json
                $data = if ($answer.privateServerInviteData) { $answer.privateServerInviteData } else { $answer }
                if ($data.placeId -and [long]$data.placeId -gt 0 -and $data.linkCode) { $invite = $data; break }
                $status = if ($answer.status) { $answer.status } elseif ($data.status) { $data.status } else { 'нет данных' }
                $problem = "share-ссылка не открывается (Роблокс: $status)"
                break
            } catch {
                $problem = $_.Exception.Message
                if ($problem -notmatch 'ошибкой 5\d\d') { break }   # 500/503 у Роблокса проходят за секунду — повторяем сразу
                Start-Sleep -Milliseconds 700
            }
        }
        if ($invite) {
            $cached = [pscustomobject]@{ PlaceId = [string]$invite.placeId; LinkCode = [string]$invite.linkCode; At = (Get-Date).ToString('o'); Stale = $false }
        } elseif ($cached) {
            # Сомнительный код, но нового Роблокс не дал — продолжаем со старым (лучше, чем не запускаться вовсе).
            $cached | Add-Member -NotePropertyName Stale -NotePropertyValue $false -Force
        } else {
            $running = Find-RunningLinkCode
            if (-not $running) { throw "$problem — вставь в настройках новую ссылку на сервер" }
            $cached = [pscustomobject]@{ PlaceId = $running.PlaceId; LinkCode = $running.LinkCode; At = (Get-Date).ToString('o'); Stale = $false }
            Write-Status "$problem. Беру код сервера из уже открытых окон и запоминаю его — он работает, пока владелец сервера его не сменит." DarkYellow $Account
        }
        $cache[$code] = $cached
        Save-LinkCache
    }
    $Account.Target.PlaceId = [string]$cached.PlaceId
    $Account.Target.LinkCode = [string]$cached.LinkCode
}

# Окно с сохранённым кодом сервера дважды подряд не смогло зайти — код мог устареть (владелец сделал новый).
# Помечаем его сомнительным: следующий запуск попробует получить новый по share-ссылке, а не выйдет — возьмёт старый.
function Set-ShareLinkStale($Account) {
    $code = $Account.Target.ShareCode
    if (-not $code) { return }
    $cached = (Get-LinkCache)[$code]
    if ($cached -and -not $cached.Stale) {
        $cached | Add-Member -NotePropertyName Stale -NotePropertyValue $true -Force
        Save-LinkCache
    }
}

#endregion

#region Аккаунты

# Ссылка по умолчанию: первая ссылка Роблокса из $LinkFile (если задан), иначе $Link из настроек, иначе — публичный
# сервер Bee Swarm Simulator.
function Get-DefaultLink {
    if ($LinkFile) {
        try {
            $text = Get-Content -LiteralPath $LinkFile -Raw -Encoding UTF8 -ErrorAction Stop
            $url = [regex]::Match($text, 'https?://(www\.)?roblox\.com/\S+').Value
            if ($url) { return $url.Trim() }
        } catch {}
    }
    if ($Link -and $Link.Trim()) { return $Link.Trim() }
    return $DefaultPlace
}

# Ссылку поменяли (в настройках или в $LinkFile) — переключаем на неё аккаунты без своей ссылки.
# Уже открытые окна не трогаем: новая ссылка применится при следующем перезаходе.
function Update-DefaultLink {
    if (((Get-Date) - $script:DefaultLinkCheckedAt).TotalSeconds -lt 10) { return }
    $script:DefaultLinkCheckedAt = Get-Date
    $url = Get-DefaultLink
    if ($url -eq $script:DefaultLink) { return }
    try { $target = Get-LaunchTarget $url } catch { Write-Status "ссылка на сервер не подходит: $($_.Exception.Message)" Red; $script:DefaultLink = $url; return }
    $script:DefaultLink = $url
    foreach ($account in @($script:Accounts) + @($script:Deferred | ForEach-Object { $_.Account })) {
        if (-not $account.UsesDefaultLink) { continue }
        $account.Link = $url
        $account.Target = Get-LaunchTarget $url
    }
    Write-Status "новая ссылка на сервер: $(Get-LinkForLog $url) — применится при следующих перезаходах." Cyan
}

# Своя ссылка аккаунта из приложения: его ссылка на сервер, иначе его игра (ID плейса), иначе '' — общая из настроек.
function Get-EntryOwnLink($Entry) {
    $link = ([string]$Entry.link).Trim()
    if ($link) { return $link }
    $game = if ($Entry.PSObject.Properties['gamePlaceId']) { ([string]$Entry.gamePlaceId).Trim() } else { '' }
    if ($game -notmatch '^\d{4,20}$') { return '' }
    $name = if ($Entry.PSObject.Properties['gameName']) { [string]$Entry.gameName } else { '' }
    if ($name) { $script:GameNames[$game] = $name }
    return $game
}

# Ссылка для журнала: код приватного сервера скрыт (журнал виден в окне приложения — его могут заскринить и отправить).
function Get-LinkForLog([string]$Url) {
    if ($Url -eq $DefaultPlace) { return "публичный сервер $DefaultGameName (ссылка не задана)" }
    if ($Url -match '^\d{4,20}$' -and $script:GameNames.ContainsKey($Url)) { return "публичный сервер $($script:GameNames[$Url])" }
    return [regex]::Replace($Url, '((?:code|linkCode|privateServerLinkCode)=)([^&#\s]+)', {
        param($match)
        $code = $match.Groups[2].Value
        $match.Groups[1].Value + '…' + $(if ($code.Length -gt 4) { $code.Substring($code.Length - 4) } else { '' })
    })
}

# vault.dat — аккаунты и логин Потассиума, JSON под защитой Windows DPAPI (CurrentUser): расшифровать его можно только
# под этой учётной записью Windows на этом ПК. Пишет его только приложение (целиком, через временный файл).
$VaultEntropy = [Text.Encoding]::UTF8.GetBytes('AutoRejoin.vault.v1')

function Read-Vault {
    if (-not (Test-Path -LiteralPath $VaultFile)) { return [pscustomobject]@{ accounts = @(); potassium = $null } }
    $bytes = [IO.File]::ReadAllBytes($VaultFile)
    $plain = [Security.Cryptography.ProtectedData]::Unprotect($bytes, $VaultEntropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $vault = [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json
    if (-not $vault.PSObject.Properties['accounts'] -or -not $vault.accounts) { $vault | Add-Member -NotePropertyName accounts -NotePropertyValue @() -Force }
    return $vault
}

# Аккаунты в работе: включённые и с кукой.
function Get-VaultEntries($Vault) {
    return @($Vault.accounts | Where-Object { $_ -and $_.id -and $_.cookie -and $_.enabled -ne $false })
}

function Get-CleanCookie([string]$Cookie) {
    return (($Cookie -replace '\s+', '').Trim('"', "'") -replace '^\.ROBLOSECURITY=', '')
}

function Get-LoginMode($Entry) {
    if ($Entry.PSObject.Properties['login'] -and [string]$Entry.login -eq 'password') { return 'password' }
    return 'cookie'
}

# Аккаунт из записи vault.dat. Бросает исключение, если своя ссылка аккаунта не подходит.
function New-VaultAccount($Entry) {
    if (-not $script:DefaultLink) { $script:DefaultLink = Get-DefaultLink; $script:DefaultLinkCheckedAt = Get-Date }
    $ownLink = Get-EntryOwnLink $Entry
    $accountLink = if ($ownLink) { $ownLink } else { $script:DefaultLink }
    $target = Get-LaunchTarget $accountLink

    [pscustomobject]@{
        Key        = [string]$Entry.id        # постоянный ключ аккаунта в приложении
        Name       = if ($Entry.name) { [string]$Entry.name } else { 'новый аккаунт' }
        UserId     = $null
        Cookie     = Get-CleanCookie $Entry.cookie
        LoginMode  = Get-LoginMode $Entry       # 'password' — новую куку приложение берёт само; 'cookie' — её вставляют руками
        ModerationNote = $null                  # что за модерация на аккаунте (403), для журнала
        Csrf       = $null
        Link       = $accountLink
        OwnLink    = $ownLink                 # своя ссылка аккаунта из приложения ('' — общая)
        UsesDefaultLink = -not $ownLink       # ссылка не своя, а общая из настроек — меняется вместе с ней
        Target     = $target
        IsMain     = [bool]$Entry.main        # мейн: свой лимит FPS, полный экран, приоритет ЦП, не сворачивается
        Tracker    = $null                # окно, которое сейчас принадлежит аккаунту
        LaunchedAt = $null                # когда запустили и ждём появления окна
        NextLaunch = [datetime]::MinValue
        Fails      = 0                    # неудачных запусков подряд
        LoginFails = 0                    # подряд окон, в которых Роблокс не принял вход (см. Register-LoginFailure)
        NoScript   = 0                    # перезапусков подряд из-за того, что скрипт не запустился
        ScriptCheckPausedUntil = $null    # до какого момента не проверять запуск скрипта (не запускался 3 раза подряд)
        MemoryWait = $false               # ждёт памяти для запуска (чтобы не писать об этом каждые 3 сек)
        LaunchDirect = $false             # последний запуск был напрямую, без Froststrap
        Fps        = 0                    # лимит FPS, с которым запущено окно
        Fullscreen = $false               # окно запущено на весь экран (мейн)
        Quality    = -1                   # графика (SavedQualityLevel), с которой запущено окно; -1 — не трогали
        QuickExits = 0                    # вылетов подряд меньше чем через минуту после захода (растёт пауза перед перезаходом)
        Restarts   = 0                    # перезаходов с запуска движка (для приложения)
        LastReason = $null                # последняя причина перезахода и когда
        LastReasonAt = $null
        FailTotal  = 0                    # неудачных попыток зайти с запуска движка (для анализа в Discord)
        JoinOk     = 0                    # удачных заходов с запуска движка
        LastError  = $null                # последняя неудачная попытка зайти: что случилось и когда
        LastErrorAt = $null
    }
}

# Поменялись мейн/хелпер или своя ссылка аккаунта в приложении.
function Update-AccountEntry($Account, $Entry) {
    $Account.LoginMode = Get-LoginMode $Entry
    $isMain = [bool]$Entry.main
    if ($Account.IsMain -ne $isMain) {
        $Account.IsMain = $isMain
        if ($Account.Tracker) {
            if (-not $isMain) { [RejoinPriority]::Release($Account.Tracker.Pid) }
            $Account.Tracker.PrioritySet = $false
        }
        $what = if ($isMain) { 'теперь мейн: FPS мейна и полный экран — со следующего захода' } else { 'теперь хелпер: FPS хелпера — со следующего захода' }
        Write-Status "$what." DarkGray $Account
    }
    $ownLink = Get-EntryOwnLink $Entry
    if ($ownLink -eq $Account.OwnLink) { return }
    $link = if ($ownLink) { $ownLink } else { $script:DefaultLink }
    try {
        $Account.Target = Get-LaunchTarget $link
        $Account.Link = $link
        $Account.OwnLink = $ownLink
        $Account.UsesDefaultLink = -not $ownLink
        Write-Status 'ссылка на сервер изменилась — применится при следующем перезаходе.' Cyan $Account
    } catch {
        $Account.OwnLink = $ownLink   # не повторять одно и то же сообщение каждые 2 сек
        Write-Status "своя ссылка аккаунта не подходит: $($_.Exception.Message) — захожу по прежней." Red $Account
    }
}

# Неудачная попытка зайти: растит паузу перед повтором и запоминает, что случилось, — приложение пишет это в Discord
# и считает, на чём именно аккаунт не может перезайти.
function Register-JoinFail($Account, [string]$Text) {
    $Account.Fails++
    $Account.FailTotal++
    $Account.LastError = $Text
    $Account.LastErrorAt = Get-Date
}

function Get-Backoff($Account) {
    $delay = [int][Math]::Min($RelaunchDelay * [Math]::Pow(2, $Account.Fails), 300)
    # Мейн заходит как можно скорее: пропал интернет — пробуем каждые $MainMaxBackoff сек, а не через 8, 16, 32...
    # (21.10 после обрыва в 09:58 мейн ждал паузы по 8 сек, а хелпер успел зайти раньше него).
    if ($Account.IsMain) { return [int][Math]::Min($delay, $MainMaxBackoff) }
    return $delay
}

#endregion

#region Окна и логи

# Читает из лога окна только новые полные строки с прошлого раза.
function Read-NewLogLines($Tracker) {
    $stream = [IO.File]::Open($Tracker.LogPath, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        if ($stream.Length -lt $Tracker.Pos) { $Tracker.Pos = 0 }
        $count = $stream.Length - $Tracker.Pos
        if ($count -le 0) { return @() }
        $null = $stream.Seek($Tracker.Pos, 'Begin')
        $buffer = New-Object byte[] $count
        $read = 0
        while ($read -lt $count) {
            $n = $stream.Read($buffer, $read, $count - $read)
            if ($n -le 0) { break }
            $read += $n
        }
    } finally { $stream.Dispose() }

    if ($read -le 0) { return @() }
    $lastNewline = [Array]::LastIndexOf($buffer, [byte]10, $read - 1)
    if ($lastNewline -lt 0) { return @() }
    $Tracker.Pos += $lastNewline + 1
    return [Text.Encoding]::UTF8.GetString($buffer, 0, $lastNewline + 1) -split "`r?`n" | Where-Object { $_ }
}

function Update-TrackerFromLog($Tracker) {
    foreach ($line in Read-NewLogLines $Tracker) {
        if ($line -match $UserIdPattern) { $Tracker.UserId = $Matches[1] }
        if ($line -match $WebLoginPattern) { $Tracker.LoginError = $Matches[1] }
        elseif ($line -match $BtidMismatchPattern) { $Tracker.BtidMismatch = $true }

        if ($line -match $JoinPattern) {
            $Tracker.PlaceId = $Matches[1]
            $Tracker.InGame = $true
            $Tracker.LastJoinAt = Get-Date
            if (-not $Tracker.EverJoined) { $Tracker.JoinedAt = Get-Date }
            $Tracker.EverJoined = $true
            $Tracker.StateSince = Get-Date
            $Tracker.LastReason = $null
            $Tracker.KickCode = $null
        }
        elseif ($line -match $KickPattern) {
            # Настоящий вылет: кик, бездействие, потеря связи. При обычном прыжке на другой сервер этой строки нет —
            # там "Disconnected ... 285" и через 0.2 сек "Teleported" и вход на новый сервер. Перезаходим без ожидания.
            $Tracker.KickCode = [int]$Matches[1]
            $Tracker.KickAt = Get-Date
            # Роблокс показал окно "Disconnected" (например 256 "Lost connection due to an unknown error"): строки
            # "Disconnected from server" после него может и не быть — окно уже не на сервере. Заход следом сбросит это.
            if ($Tracker.InGame) {
                $Tracker.InGame = $false
                $Tracker.StateSince = Get-Date
            }
        }
        elseif ($line -match $DisconnectPattern) {
            if ($Matches.r) { $Tracker.LastReason = $Matches.r.Trim() }
            elseif ($line -match 'leaveUGCGameInternal') { $Tracker.LastReason = 'вышел в меню' }
            if ($Tracker.InGame) {
                $Tracker.InGame = $false
                $Tracker.StateSince = Get-Date
            }
        }
    }
}

# Лог создаётся через 1-3 сек после старта окна; окна разбираем по порядку запуска, чтобы каждое взяло свой.
# Чужой лог не берём: 12.11 с 09:56 до 12:54 окно мейна каждый раз брало лог хелпера, закрытого за секунду до запуска
# мейна ("уступил очередь мейну"). В том логе захода нет, и мейн 3 часа подряд "не заходил на сервер за 60 сек", хотя
# был в игре. Поэтому лог, который уже был у закрытого окна, второй раз не выдаём (Add-UsedLog); свой лог процесс создаёт
# сам, после старта (12.11: окно стартовало в 08:54:04.750, его лог создан в 08:54:05.719); и старт процесса, записанный
# в самом логе (Get-LogProcessStart, с точностью до секунды), должен совпасть со стартом окна.
function Find-TrackerLog($Tracker) {
    $claimed = @($script:Trackers.Values | ForEach-Object { $_.LogPath } | Where-Object { $_ })
    $files = @(Get-ChildItem -LiteralPath $LogDir -Filter '*_Player_*_last.log' -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -notlike '*CrashHandler*' -and $claimed -notcontains $_.FullName -and -not $script:UsedLogs.ContainsKey($_.FullName) -and
            $_.CreationTime -ge $Tracker.Start.AddSeconds(-2) -and $_.CreationTime -le $Tracker.Start.AddSeconds(20)
        } |
        Sort-Object CreationTime)
    $startUtc = $Tracker.Start.ToUniversalTime()
    foreach ($file in $files) {
        if ($Tracker.StartGuessed) { return $file.FullName }
        if ($file.CreationTimeUtc -lt $startUtc.AddSeconds(-0.25)) { continue }   # создан раньше, чем окно стартовало, — не его
        $logStart = Get-LogProcessStart $file.FullName
        if ($logStart -is [string]) { return $null }          # 'wait': строки со временем ещё не записаны — в следующий раз
        if ($null -eq $logStart) { return $file.FullName }    # другой формат лога — как раньше, первый по времени
        # Часы лога Роблокс заводит позже старта процесса — под нагрузкой на 2 сек и больше (12.11 в 13:40 окно запущено около
        # 09:40:01.3 UTC, а по логу стартовало в 09:40:03; с границей "+1.5 сек" его лог не взяли, окно осталось без лога, и
        # движок не видел, что оно в игре). Поэтому только нижняя граница: раньше старта окна больше чем на секунду
        # (округление) — это лог процесса, который стартовал раньше.
        if (($logStart - $startUtc).TotalSeconds -ge -1.0) { return $file.FullName }
    }
    return $null
}

# Старт процесса, который пишет лог Роблокса (UTC). Строки лога начинаются с "2026-11-12T05:56:07.187Z,0.187394,": время
# записи и сколько секунд прошло со старта часов лога. Они заводятся после старта процесса и округлены до секунды (12.11:
# процесс стартовал в 08:54:04.750, по логу — 08:54:05.000; в 13:40 под нагрузкой — почти на 2 сек позже). 'wait' — лог только что
# создан и строк со временем в нём ещё нет; $null — таких строк нет (другой формат) или файл не читается.
function Get-LogProcessStart([string]$Path) {
    $text = ''
    try {
        $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
        try {
            $buffer = New-Object byte[] 4096
            $read = $stream.Read($buffer, 0, $buffer.Length)
            $text = [Text.Encoding]::UTF8.GetString($buffer, 0, [Math]::Max($read, 0))
        } finally { $stream.Dispose() }
    } catch { return $null }
    $match = [regex]::Match($text, '(?m)^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z),(\d+(?:\.\d+)?),')
    if ($match.Success) {
        $culture = [Globalization.CultureInfo]::InvariantCulture
        $at = [datetime]::Parse($match.Groups[1].Value, $culture, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
        return $at.AddSeconds(-[double]::Parse($match.Groups[2].Value, $culture))
    }
    $age = try { ((Get-Date) - [IO.File]::GetCreationTime($Path)).TotalSeconds } catch { 999 }
    if ($text.Length -lt 4096 -and $age -lt 5) { return 'wait' }
    return $null
}

# Окно закрылось — его лог больше ничей: другому окну его не отдаём (Find-TrackerLog). Помним 30 минут — дольше не нужно:
# лог берут только окна, стартовавшие не позже чем через 2 сек после его создания.
function Add-UsedLog($Tracker) {
    if (-not $Tracker -or -not $Tracker.LogPath) { return }
    $now = Get-Date
    $script:UsedLogs[[string]$Tracker.LogPath] = $now
    foreach ($path in @($script:UsedLogs.Keys)) {
        if (($now - $script:UsedLogs[$path]).TotalMinutes -gt 30) { $script:UsedLogs.Remove($path) }
    }
}

# С 08.10 Роблокс (эксперимент "Tray treatment") при закрытии окна игры запускает вместо него фоновый процесс
# "RobloxPlayerBeta --launch-to-tray". Он занимает ROBLOX_singletonEvent (из-за этого не включался режим нескольких окон),
# перехватывает новые запуски ("WebPlay NoReload with Tray only") и выглядел для скрипта как грузящееся окно,
# из-за чего Attach откладывался. Пустые такие процессы закрываем сразу — игры в них нет.
function Test-IdleTray($Process, [string]$CommandLine) {
    if ($CommandLine -notmatch '--launch-to-tray') { return $false }
    $memoryGB = try { $Process.PrivateMemorySize64 / 1GB } catch { 0 }
    return $memoryGB -lt 0.6   # в игре окно занимает от 1.5 ГБ; маленький — значит пустой фоновый процесс
}

function Stop-IdleTrays {
    $stopped = 0
    foreach ($info in @(Get-CimInstance Win32_Process -Filter "Name='$PlayerProcess.exe'" -ErrorAction SilentlyContinue)) {
        $process = Get-Process -Id $info.ProcessId -ErrorAction SilentlyContinue
        if ($process -and (Test-IdleTray $process $info.CommandLine)) {
            Stop-Process -Id $info.ProcessId -Force -ErrorAction SilentlyContinue
            $stopped++
        }
    }
    return $stopped
}

function Update-Trackers {
    $live = @(Get-Process -Name $PlayerProcess -ErrorAction SilentlyContinue)
    $liveIds = @($live | ForEach-Object { $_.Id })

    foreach ($id in @($script:Trackers.Keys)) {
        if ($liveIds -notcontains $id) {
            $script:Trackers[$id].Alive = $false
            Add-UsedLog $script:Trackers[$id]
            $script:Trackers.Remove($id)
            if (-not $script:TrayDirtyAt) { $script:TrayDirtyAt = Get-Date }   # от него мог остаться значок в трее
        }
    }

    foreach ($id in @($script:KilledPids.Keys)) {
        if ($liveIds -notcontains $id) { $script:KilledPids.Remove($id) }
    }

    foreach ($process in $live) {
        $tracker = $script:Trackers[$process.Id]
        if (-not $tracker) {
            $startGuessed = $false   # время старта процесса не узнать — тогда и лог окна по нему не сверить (Find-TrackerLog)
            $start = try { $process.StartTime } catch { $startGuessed = $true; Get-Date }
            # Окно, которое мы только что закрыли для перезахода, ещё не исчезло — не берём его в работу.
            if ($script:KilledPids.ContainsKey($process.Id) -and $start -le $script:KilledPids[$process.Id].AddSeconds(1)) { continue }
            $commandLine = try { (Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)" -ErrorAction Stop).CommandLine } catch { '' }
            $tracker = [pscustomobject]@{
                Pid = $process.Id; Start = $start; StartGuessed = $startGuessed; Process = $process; Alive = $true
                IsTray = $commandLine -match '--launch-to-tray'; CommandLine = $commandLine
                LogPath = $null; Pos = 0; UserId = $null; PlaceId = $null
                InGame = $false; EverJoined = $false; StateSince = (Get-Date); LastReason = $null
                HangSince = $null; Announced = $false; ForceReason = $null
                Attached = $false; AttachedAt = $null; ScriptSeen = $false; ScriptSeenAt = $null; ScriptVia = $null
                Compacted = $false; Recompacted = 0; JoinedAt = $null
                FullscreenTries = 0; FullscreenAt = $null
                KickCode = $null; KickAt = $null; PrioritySet = $false; LastJoinAt = $null
                LoginError = $null; BtidMismatch = $false   # Роблокс не принял вход в окне (см. $WebLoginPattern)
                Adopted = $false; UserOpen = $false; SeenOpen = $false; Restores = @()
                MemoryHeld = $false; MemoryFailed = $false   # Set-HelperMemory: потолок RAM стоит / не поставился
                ManualAttach = $null; ManualAttachNote = $null   # Invoke-ManualAttach: 'pressed' / 'nobutton'; почему не жали
            }
            $script:Trackers[$process.Id] = $tracker
        }
        $tracker.Process = $process
    }

    # Пустые фоновые процессы "в трее" закрываем сразу (см. Test-IdleTray).
    foreach ($tracker in @($script:Trackers.Values | Where-Object { $_.IsTray -and -not $_.EverJoined })) {
        if ((Get-Owner $tracker) -or -not (Test-IdleTray $tracker.Process $tracker.CommandLine)) { continue }
        Stop-Process -Id $tracker.Pid -Force -ErrorAction SilentlyContinue
        Add-UsedLog $tracker
        $script:Trackers.Remove($tracker.Pid)
        $liveIds = @($liveIds | Where-Object { $_ -ne $tracker.Pid })
        if (-not $script:TrayWarned) {
            $script:TrayWarned = $true
            Write-Status 'Закрываю фоновый процесс Роблокса "в трее" — новая версия Роблокса запускает его вместо закрытого окна, и он мешает. Дальше закрываю такие молча.' DarkGray
        }
    }

    if ($liveIds) {
        try {
            foreach ($id in [RejoinWindows]::PidsWithTitle([int[]]$liveIds, [string[]]$CrashTitles)) {
                $tracker = $script:Trackers[$id]
                if ($tracker -and -not $tracker.ForceReason) { $tracker.ForceReason = 'Роблокс упал (окно с ошибкой)' }
            }
        } catch { Write-Status "проверка окон с ошибкой: $($_.Exception.Message)" DarkYellow }
    }

    foreach ($tracker in @($script:Trackers.Values | Sort-Object Start)) {
        if (-not $tracker.LogPath) { $tracker.LogPath = Find-TrackerLog $tracker }
        if ($tracker.LogPath) {
            try { Update-TrackerFromLog $tracker }
            catch { Write-Status "не читается лог окна $($tracker.Pid): $($_.Exception.Message)" DarkYellow; $tracker.LogPath = $null }
        } elseif (-not $tracker.IsTray -and -not $tracker.LogMissingNoted -and ((Get-Date) - $tracker.Start).TotalSeconds -ge 30) {
            # Без лога не видно ни захода, ни вылета, и Attach за автоаттач не жмётся (12.11 в 13:40 так никто и не узнал,
            # что окно мейна в игре без скрипта) — пусть хотя бы будет видно в журнале.
            $tracker | Add-Member -NotePropertyName LogMissingNoted -NotePropertyValue $true -Force
            $owner = Get-Owner $tracker
            if ($owner) { Write-Status 'не нашёл лог окна — не вижу, зашло ли оно на сервер, и Attach за автоаттач не нажму; слежу только за закрытием и зависанием.' DarkYellow $owner }
        }
    }
}

function Get-Owner($Tracker) {
    $script:Accounts | Where-Object { $_.Tracker -and $_.Tracker.Pid -eq $Tracker.Pid } | Select-Object -First 1
}

# Можно ли запускать следующее окно. Окна заходят по очереди: пока последнее запущенное окно не зашло на сервер
# и в нём не запустился скрипт Hive (его подключает автоаттач Потассиума), следующее не запускаем, а после
# запуска скрипта ждём ещё $LaunchCooldown сек. Так Потассиум подключается к окнам по одному.
function Test-LaunchBusy {
    # Предыдущее окно ещё не появилось — ждём, иначе можно перепутать, какое окно чьё.
    if ($script:Accounts | Where-Object { $_.LaunchedAt }) { return $true }
    foreach ($account in $script:Accounts) {
        $tracker = $account.Tracker
        if (-not $tracker -or $tracker.ForceReason) { continue }   # окно и так сейчас перезапустится
        if (-not $tracker.EverJoined) { return $true }              # ещё грузится (не зайдёт за $JoinTimeout — перезапуск)
        if (-not (Test-ScriptWaited $account $tracker)) { return $true }
    }
    if (((Get-Date) - $script:LastReadyAt).TotalSeconds -lt $LaunchCooldown) { return $true }
    # Свежее окно, чей аккаунт ещё неизвестен (например, открыли руками), — подождём, вдруг это один из наших.
    $unknown = $script:Trackers.Values | Where-Object {
        -not $_.IsTray -and -not $_.UserId -and ((Get-Date) - $_.Start).TotalSeconds -lt 45 -and -not (Get-Owner $_)
    }
    return [bool]$unknown
}

# Мейн ещё не в игре со скриптом (не запущен, грузится, ждёт перезахода): окна остальных аккаунтов не запускаем, чтобы
# мейн заходил первым и ни с кем не делил интернет, память и Потассиум. Ждём его не дольше $MainFirstMax сек подряд:
# мейн, который не заходит (бан, устаревшая кука), не должен держать хелперов. Мейна нет в работе — не ждём.
function Test-MainPending {
    $mains = @($script:Accounts | Where-Object { $_.IsMain })
    $pending = $false
    foreach ($main in $mains) {
        $tracker = $main.Tracker
        if (-not $tracker -or $tracker.ForceReason -or -not $tracker.EverJoined -or -not (Test-ScriptWaited $main $tracker)) { $pending = $true }
    }
    # Мейн выпал из очереди из-за куки, и приложение прямо сейчас берёт ему новую (обычно 1–3 сек) — тоже ждём его.
    foreach ($asked in @($script:AuthAsked.Values)) {
        if ($asked.IsMain -and ((Get-Date) - $asked.At).TotalSeconds -lt 60 -and -not ($script:Accounts | Where-Object { $_.Key -eq $asked.Key })) { $pending = $true }
    }
    if (-not $pending) { $script:MainPendingSince = $null; return $false }
    if (-not $script:MainPendingSince) { $script:MainPendingSince = Get-Date }
    return ((Get-Date) - $script:MainPendingSince).TotalSeconds -lt $MainFirstMax
}

# Мейну пора заходить, а очередь занята хелпером, который ещё грузится или ждёт скрипт: закрываем его (зайдёт после
# мейна), чтобы мейн не ждал. Окно хелпера, которое ещё не появилось (секунда-две), не трогаем — его не отличить.
function Clear-LoadingHelpers {
    foreach ($account in @($script:Accounts | Where-Object { -not $_.IsMain })) {
        $tracker = $account.Tracker
        if (-not $tracker -or $tracker.ForceReason) { continue }
        if (-not $tracker.EverJoined -or -not (Test-ScriptWaited $account $tracker)) {
            Write-Status 'ещё грузится, а мейну пора заходить — закрываю, зайду после мейна.' DarkYellow $account
            $account.LastReason = 'уступил очередь мейну'
            $account.LastReasonAt = Get-Date
            Stop-AccountWindow $account
            $account.NextLaunch = Get-Date
        }
    }
    # Кулдаун после запуска скрипта у хелпера мейна не держит.
    $script:LastReadyAt = [datetime]::MinValue
    # Удержание FPS закрытого хелпера тоже: его окна уже нет, а у мейна другой лимит — иначе мейн ждал конца
    # удержания, хелпер за это время запускался снова и продлевал его (24.10 так мейн простоял с 01:12 до 09:51).
    # Окно, которое ещё не появилось, удержание держит (его не закрываем, и мейн до его появления не запустится).
    if (-not ($script:Accounts | Where-Object { $_.LaunchedAt })) { $script:FpsHoldUntil = $null }
}

# Мейну пора запускаться прямо сейчас: окна нет, оно не запускается, пауза перед перезаходом прошла. Тогда хелперы
# не запускаются, даже если мейн ждёт дольше $MainFirstMax: очередь сейчас его, а не их.
function Test-MainDue {
    if ($script:LaunchBlocked) { return $false }
    $now = Get-Date
    foreach ($main in @($script:Accounts | Where-Object { $_.IsMain })) {
        if (-not $main.Tracker -and -not $main.LaunchedAt -and $now -ge $main.NextLaunch) { return $true }
    }
    return $false
}

# Порядок обхода: мейн первым, чтобы в одном цикле его запуск шёл раньше остальных.
function Get-AccountsMainFirst {
    return @(@($script:Accounts | Where-Object { $_.IsMain }) + @($script:Accounts | Where-Object { -not $_.IsMain }))
}

# Файлы, которые скрипт Hive (загрузчик javad1khive.txt в автоэкзекуте Потассиума) пишет в workspace:
#   javad1khive_started_<UserId>.txt — скрипт запустился в окне (внутри "ok" или почему не смог);
#   javad1khive_alive_<UserId>.txt   — пульс, раз в 10 сек;
#   javad1khive_error_<UserId>.txt   — Роблокс показал ошибку (кик, дисконнект и т.п.), внутри её текст.
# Список файлов читаем не чаще раза в 2 сек. Возвращает время записи файла или $null.
# Atlas, запущенный напрямую (без нашего загрузчика), этих файлов не пишет, но сам пишет в workspace
# atlas-<ник>.json (пресет при загрузке) и atlascache-<ник>.json (таймеры мобов, диспенсеров, материалов) —
# их тоже собираем: по ним видно, что скрипт в окне есть (см. Get-AtlasFileTime).
function Update-WorkspaceFiles {
    $now = Get-Date
    if (($now - $script:WorkspaceScanAt).TotalSeconds -lt 2) { return }
    $script:WorkspaceScanAt = $now
    $files = @{}
    foreach ($filter in @('javad1khive_*.txt', 'atlas*.json')) {
        foreach ($file in @(Get-ChildItem -LiteralPath $Workspace -Filter $filter -File -ErrorAction SilentlyContinue)) {
            $files[$file.Name] = $file.LastWriteTime
        }
    }
    # Пресеты Atlas (atlas\<пресет>.json) — Atlas сохраняет пресет аккаунта, как только загрузился в окне.
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $Workspace 'atlas') -Filter '*.json' -File -ErrorAction SilentlyContinue)) {
        $files["atlas\$($file.Name)"] = $file.LastWriteTime
    }
    $script:WorkspaceFiles = $files
}

function Get-ScriptFileTime([string]$Kind, $Account) {
    Update-WorkspaceFiles
    return $script:WorkspaceFiles["javad1khive_${Kind}_$($Account.UserId).txt"]
}

# Последняя запись Atlas в workspace для этого аккаунта (по нику Роблокса) или $null.
function Get-AtlasFileTime($Account) {
    if (-not $Account.Name) { return $null }
    Update-WorkspaceFiles
    $latest = $null
    $names = @("atlas-$($Account.Name).json", "atlascache-$($Account.Name).json")
    # В atlas-<ник>.json Atlas пишет имя пресета этого аккаунта ("Preset 3.json"): пресет он сохраняет сразу при
    # загрузке, а atlascache — только когда сработает таймер, иногда через много минут.
    if ($script:WorkspaceFiles["atlas-$($Account.Name).json"]) {
        $preset = try { ([IO.File]::ReadAllText((Join-Path $Workspace "atlas-$($Account.Name).json"))).Trim() } catch { '' }
        if ($preset -and $preset -notmatch '[\\/:]') {
            if ($preset -notmatch '\.json$') { $preset += '.json' }
            $names += "atlas\$preset"
        }
    }
    foreach ($name in $names) {
        $time = $script:WorkspaceFiles[$name]
        if ($time -and (-not $latest -or $time -gt $latest)) { $latest = $time }
    }
    return $latest
}

function Read-ScriptFile([string]$Kind, $Account) {
    try {
        $text = [IO.File]::ReadAllText((Join-Path $Workspace "javad1khive_${Kind}_$($Account.UserId).txt"))
        $text = ($text -replace '\s+', ' ').Trim()
        if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + '...' }
        return $text
    } catch { return '' }
}

# Отмечает, что в окне запустился скрипт, и ловит ошибки Роблокса, о которых сообщил скрипт. Файлы старше самого
# окна — от прошлого окна этого аккаунта, их не считаем.
function Update-ScriptState($Account) {
    $tracker = $Account.Tracker
    if (-not $tracker -or $NoPotassium) { return }
    $since = $tracker.Start.AddSeconds(-2)

    if (-not $tracker.ScriptSeen) {
        $started = Get-ScriptFileTime 'started' $Account
        # Atlas, запущенный без нашего загрузчика: файла started нет, но Atlas пишет свои файлы после старта окна.
        $atlas = Get-AtlasFileTime $Account
        if (-not ($started -and $started -ge $since) -and $atlas -and $atlas -ge $tracker.Start.AddSeconds(5)) {
            $tracker.ScriptSeen = $true
            $tracker.ScriptSeenAt = Get-Date
            $tracker.ScriptVia = 'atlas'
            Set-TrackerAttached $tracker
            $script:LastReadyAt = Get-Date
            $Account.NoScript = 0
            Write-Status "скрипт работает (Atlas пишет свои файлы в workspace $(if ($Executor -ne 'potassium') { $script:ExecName } else { 'Потассиума' }))." Green $Account
        }
        elseif ($started -and $started -ge $since) {
            $tracker.ScriptSeen = $true
            $tracker.ScriptSeenAt = Get-Date
            Set-TrackerAttached $tracker
            $script:LastReadyAt = Get-Date
            $Account.NoScript = 0
            $note = Read-ScriptFile 'started' $Account
            if ($note -and $note -ne 'ok') {
                Write-Status "автоэкзекут сработал, но скрипт не запустился: $note" Red $Account
            } else {
                $after = if ($tracker.JoinedAt) { ' ({0:N0} сек после захода)' -f ((Get-Date) - $tracker.JoinedAt).TotalSeconds } else { '' }
                $how = if ($tracker.ManualAttach -eq 'pressed') { ' — после нашего Attach: автоаттач Потассиума сам не подключился' } else { '' }
                Write-Status "скрипт запустился$after$how." Green $Account
            }
        }
    }

    if (-not $tracker.ForceReason) {
        $errorAt = Get-ScriptFileTime 'error' $Account
        # Ошибка старше последнего запуска скрипта — окно после неё переподключилось само (кнопка Reconnect) и работает:
        # не перезаходим (24.10 в 00:22 так закрыли рабочий мейн из-за ошибки 23:25).
        $startedAt = Get-ScriptFileTime 'started' $Account
        if ($errorAt -and $errorAt -ge $since -and -not ($startedAt -and $startedAt -gt $errorAt)) {
            $text = Read-ScriptFile 'error' $Account
            if (-not $text) { $text = 'без текста' }
            $tracker.ForceReason = "ошибка в Роблоксе: $text"
        }
    }
}

# День в имени файла лога данных Atlas (os.date("%Y-%m-%d") — местная дата этого ПК).
function Get-DataLogDay { return (Get-Date).ToString('yyyy-MM-dd') }

# Дочитывает файл лога данных Atlas с байта $Pos до последнего полного перевода строки. Возвращает текст и новую позицию;
# недописанную строку оставляет на следующий раз. Файла нет — пустой текст, позиция та же.
function Read-DataLogTail([string]$Path, [long]$Pos) {
    $stream = $null
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        if ($stream.Length -lt $Pos) { $Pos = 0 }   # файл переписан заново
        $count = $stream.Length - $Pos
        if ($count -le 0) { return @('', $Pos) }
        $bytes = New-Object byte[] $count
        $null = $stream.Seek($Pos, [IO.SeekOrigin]::Begin)
        $read = 0
        while ($read -lt $count) {
            $n = $stream.Read($bytes, $read, $count - $read)
            if ($n -le 0) { break }
            $read += $n
        }
        $last = [Array]::LastIndexOf($bytes, [byte]10, $read - 1)
        if ($read -le 0 -or $last -lt 0) { return @('', $Pos) }
        return @([Text.Encoding]::UTF8.GetString($bytes, 0, $last + 1), ($Pos + $last + 1))
    } catch {
        return @('', $Pos)
    } finally {
        if ($stream) { $stream.Dispose() }
    }
}

# Перезаход ВСЕХ окон после $BalloonRejoinCount продлений Balloon Blessing МЕЙНА: Atlas пишет
# "BUF <unix> ev=r n=Balloon Blessing ..." в лог данных, когда полоска баффа подскочила (шар в улье сдан). Считаем только
# продления мейна (у хелперов шар маленький, он сдаётся в 4–5 раз чаще) с начала цикла: запуск движка (после утренней
# перезагрузки) или последний перезаход всеми окнами. Кик мейна счёт не сбрасывает — иначе мейн и хелперы разъезжаются.
# На N-м — сразу перезаход всех окон: шар только что сдан, ничего не теряется. Но если до планового перезапуска
# (расписание) меньше $BalloonQuietMinutes — не перезаходим: перезапуск по расписанию всё равно скоро всё обновит, а лишний
# перезаход за час до него — потерянные минуты. Файл читаем раз в 5 сек, с того места, где остановились; в полночь
# дочитываем хвост вчерашнего и переходим на файл нового дня.
$BalloonRefreshPattern = '(?m)^BUF\t(\d+(?:\.\d+)?)\tev=r\tn=Balloon Blessing\t'
$script:BalloonCycle = $null

# Новый цикл счёта: с этого момента. Место в файле лога сохраняется — строки до начала цикла отсекаются по времени.
function Reset-BalloonCycle {
    $since = ([DateTimeOffset](Get-UtcNow)).ToUnixTimeSeconds()
    if ($script:BalloonCycle) {
        $script:BalloonCycle.Since = $since; $script:BalloonCycle.Count = 0; $script:BalloonCycle.Quiet = $false
    } else {
        $script:BalloonCycle = [pscustomobject]@{ Since = $since; Count = 0; Quiet = $false; UserId = $null; File = $null; Pos = 0; At = [datetime]::MinValue }
    }
}

# Сколько минут до планового перезапуска; $null — расписание выключено.
function Get-MinutesToSchedule {
    if (-not $script:ScheduleNextUtc) { return $null }
    return ($script:ScheduleNextUtc - (Get-UtcNow)).TotalMinutes
}

function Update-BalloonRejoin($Account) {
    if ($BalloonRejoinCount -le 0 -or $NoPotassium -or -not $Account.IsMain -or -not $Account.UserId) { return }
    if (-not $script:BalloonCycle) { Reset-BalloonCycle }
    $cycle = $script:BalloonCycle
    if ($cycle.Quiet) { return }
    $now = Get-Date
    if (($now - $cycle.At).TotalSeconds -lt 5) { return }
    $cycle.At = $now
    # Мейна сменили — считаем его лог с начала цикла.
    if ([string]$cycle.UserId -ne [string]$Account.UserId) { $cycle.UserId = $Account.UserId; $cycle.File = $null; $cycle.Pos = 0; $cycle.Count = 0 }

    $today = Join-Path $Workspace "javad1khive_data_$($Account.UserId)_$(Get-DataLogDay).tsv"
    $files = @()
    if ($cycle.File -and $cycle.File -ne $today) { $files += $cycle.File }
    $files += $today
    $added = 0
    foreach ($file in $files) {
        if ($file -ne $cycle.File) { $cycle.File = $file; $cycle.Pos = 0 }
        $tail = Read-DataLogTail $file $cycle.Pos
        $cycle.Pos = $tail[1]
        foreach ($match in [regex]::Matches($tail[0], $BalloonRefreshPattern)) {
            $at = [double]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
            if ($at -ge $cycle.Since) { $added++ }
        }
    }
    if (-not $added) { return }
    $cycle.Count += $added
    if ($cycle.Count -lt $BalloonRejoinCount) {
        Write-Status "Balloon Blessing продлён: $($cycle.Count) из $BalloonRejoinCount — на $BalloonRejoinCount-м перезайдут все окна." DarkGray $Account
        return
    }
    $left = Get-MinutesToSchedule
    if ($null -ne $left -and $left -lt $BalloonQuietMinutes) {
        $cycle.Quiet = $true
        $what = if ($script:Schedule.Action -eq 'reboot') { 'плановой перезагрузки' } else { 'планового перезахода' }
        Write-Status ('{0} продлений Balloon Blessing, но до {1} в {2} осталось {3:N0} мин — не перезахожу, его дождёмся.' -f $cycle.Count, $what, $script:Schedule.Time, [Math]::Max($left, 0)) Cyan $Account
        return
    }
    Invoke-RejoinAll "$($cycle.Count) продлений Balloon Blessing мейна (перезаход всех каждые $BalloonRejoinCount)" 'перезаход по Balloon Blessing мейна'
    Reset-BalloonCycle   # и когда новые окна не запускаются (LaunchBlocked) — чтобы не повторять на каждом продлении
}

# Ждать ли ещё скрипт в окне: окно не сворачиваем и следующее не запускаем, пока в нём не запустился скрипт —
# но не дольше $ScriptStartTimeout сек после захода (дальше окно перезапустит Get-RestartReason).
function Test-WaitScript($Tracker) {
    if ($ScriptStartTimeout -le 0 -or $NoPotassium -or $Tracker.ScriptSeen) { return $false }
    if (-not $Tracker.JoinedAt) { return $true }
    return ((Get-Date) - $Tracker.JoinedAt).TotalSeconds -lt $ScriptStartTimeout
}

# Для очереди запусков: дождались скрипта в окне (или ждать его в этом аккаунте сейчас не будем).
function Test-ScriptWaited($Account, $Tracker) {
    if (-not (Test-WaitScript $Tracker)) { return $true }
    return [bool]($Account.ScriptCheckPausedUntil -and (Get-Date) -lt $Account.ScriptCheckPausedUntil)
}

# Скрипт в окне перестал работать: пульса нет дольше $ScriptTimeout (окно зависло внутри игры, скрипт упал и т.п.).
# Если окно подряд перезапускается без скрипта, срок растёт (×2, ×4, ×8), чтобы не дёргать окна, пока не починят.
function Test-ScriptMissing($Account) {
    $tracker = $Account.Tracker
    if ($ScriptTimeout -le 0 -or $NoPotassium -or -not $tracker -or -not $tracker.ScriptSeen) { return $false }
    # Atlas без нашего загрузчика пульса не пишет — по тишине такое окно не перезапускаем.
    if ($tracker.ScriptVia -eq 'atlas' -and -not (Get-ScriptFileTime 'alive' $Account)) { return $false }
    $limit = $ScriptTimeout * [Math]::Pow(2, [Math]::Min($Account.NoScript, 3))
    $last = $tracker.ScriptSeenAt
    $pulse = Get-ScriptFileTime 'alive' $Account
    if ($pulse -and $pulse -gt $last) { $last = $pulse }
    return ((Get-Date) - $last).TotalSeconds -ge $limit
}

# Роблокс упал так, что Windows показала "Прекращена работа программы" (WerFault.exe): процесс Роблокса висит,
# пока это окно не закроют. Закрываем его и перезапускаем окно Роблокса. Ищем раз в 5 сек.
function Find-WerCrashes {
    $now = Get-Date
    if (($now - $script:WerScanAt).TotalSeconds -lt 5 -or -not $script:Trackers.Count) { return }
    $script:WerScanAt = $now
    foreach ($wer in @(Get-CimInstance Win32_Process -Filter "Name='WerFault.exe'" -ErrorAction SilentlyContinue)) {
        if ($wer.CommandLine -notmatch '\s-p\s+(\d+)') { continue }
        $tracker = $script:Trackers[[int]$Matches[1]]
        if (-not $tracker) { continue }
        Stop-Process -Id $wer.ProcessId -Force -ErrorAction SilentlyContinue
        if (-not $tracker.ForceReason) { $tracker.ForceReason = 'Роблокс упал (Windows: "прекращена работа программы")' }
    }
}

# Свободная память вместе с файлом подкачки (то, что ещё можно выделить), ГБ. $null — не удалось узнать.
function Get-FreeMemoryGB {
    if (((Get-Date) - $script:FreeMemoryAt).TotalSeconds -ge 5) {
        $script:FreeMemoryAt = Get-Date
        $script:FreeMemoryGB = try { (Get-CimInstance Win32_OperatingSystem).FreeVirtualMemory / 1MB } catch { $null }
    }
    return $script:FreeMemoryGB
}

# Память почти кончилась — перезапускаем самое большое окно, пока Windows не уронила все.
# Порог низкий и между такими перезапусками минута: окна обычного размера при перезапуске снова
# вырастают до того же, и с высоким порогом окна перезапускались по кругу каждые 15 сек.
# Если памяти просто не хватает на все окна, лишние ждут её в Test-LaunchMemory, а не крутятся.
function Test-LowMemory {
    if ($MinFreeMemoryGB -le 0) { return }
    if (((Get-Date) - $script:LastMemoryRestart).TotalSeconds -lt 60) { return }
    $freeGB = Get-FreeMemoryGB
    if ($null -eq $freeGB -or $freeGB -ge $MinFreeMemoryGB) { return }

    $biggest = @($script:Accounts | Where-Object { $_.Tracker } | ForEach-Object { $_.Tracker }) |
        Sort-Object { try { $_.Process.PrivateMemorySize64 } catch { 0 } } -Descending |
        Select-Object -First 1
    if ($biggest -and -not $biggest.ForceReason) {
        $script:LastMemoryRestart = Get-Date
        $size = try { $biggest.Process.PrivateMemorySize64 / 1GB } catch { 0 }
        $biggest.ForceReason = 'в системе осталось {0:N1} ГБ свободной памяти, окно занимает {1:N1} ГБ' -f $freeGB, $size
    }
}

# Хватит ли памяти на ещё одно окно. Окно, открытое без памяти, не заходит на сервер или валит соседей.
function Test-LaunchMemory($Account) {
    if ($WindowMemoryGB -le 0) { return $true }
    $freeGB = Get-FreeMemoryGB
    $needGB = $WindowMemoryGB + [Math]::Max($MinFreeMemoryGB, 0)
    if ($null -eq $freeGB -or $freeGB -ge $needGB) {
        if ($Account.MemoryWait) { Write-Status ('память освободилась ({0:N1} ГБ).' -f $freeGB) Cyan $Account }
        $Account.MemoryWait = $false
        return $true
    }
    if (-not $Account.MemoryWait) {
        $Account.MemoryWait = $true
        Write-Status ('жду памяти для окна: свободно {0:N1} ГБ, нужно {1:N1} ГБ. Если так постоянно — нажми «Настроить подкачку» в настройках.' -f $freeGB, $needGB) DarkYellow $Account
    }
    return $false
}

# При старте: хватит ли RAM + файла подкачки на все окна. Если нет — окна будут вылетать по памяти.
function Test-MemoryCapacity {
    if ($WindowMemoryGB -le 0) { return }
    try { $limitGB = (Get-CimInstance Win32_OperatingSystem).TotalVirtualMemorySize / 1MB } catch { return }
    $needGB = Get-NeededCommitGB $script:Accounts.Count
    if ($limitGB -ge $needGB) { return }
    Write-Status ('Памяти (RAM + файл подкачки) {0:N1} ГБ, а на {1} окон нужно около {2:N0} ГБ. Все окна разом не поместятся: часть будет ждать памяти.' -f $limitGB, $script:Accounts.Count, $needGB) Yellow
    Write-Status 'Чтобы влезли все, один раз нажми «Настроить подкачку» в настройках и перезагрузи компьютер.' Yellow
}

# Сколько всего нужно памяти (с подкачкой) на $Count окон: обычный размер каждого, запас на одно раздувшееся
# до $MemoryLimitGB и ~6 ГБ на Windows, Потассиум и браузер.
function Get-NeededCommitGB([int]$Count) {
    $window = if ($WindowMemoryGB -gt 0) { $WindowMemoryGB } else { 3.5 }
    return $Count * $window + [Math]::Max($MemoryLimitGB - $window, 0) + 6
}

# Где лежит Потассиум: -PotassiumPath, запущенный процесс, прошлый раз (potassium.path), обычные места.
# Найденный путь запоминается в potassium.path — после перезагрузки, когда Потассиум ещё не открыт, он уже известен.
function Get-PotassiumExe {
    $candidates = @($PotassiumPath)
    foreach ($process in @(Get-Process -Name Potassium -ErrorAction SilentlyContinue)) {
        try { $candidates += $process.Path } catch {}
    }
    $candidates += $script:PotassiumExe
    try { $candidates += (Get-Content -LiteralPath $PotassiumPathFile -TotalCount 1 -Encoding UTF8 -ErrorAction Stop).Trim() } catch {}
    foreach ($root in [Environment]::GetFolderPath('Desktop'), (Join-Path $env:USERPROFILE 'Downloads'), $env:LOCALAPPDATA) {
        $candidates += Join-Path $root 'Potassium\Potassium.exe'
    }

    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        # bin\Potassium.exe — внутренний модуль, открывать надо основной Potassium.exe уровнем выше.
        $folder = Split-Path $candidate
        if ((Split-Path $folder -Leaf) -eq 'bin') {
            $outer = Join-Path (Split-Path $folder) 'Potassium.exe'
            if (Test-Path -LiteralPath $outer -PathType Leaf) { $candidate = $outer }
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        if ($candidate -ne $script:PotassiumExe) {
            $script:PotassiumExe = $candidate
            try { Set-Content -LiteralPath $PotassiumPathFile -Value $candidate -Encoding UTF8 } catch {}
        }
        return $candidate
    }
    return $null
}

function Start-Potassium([string]$Path) {
    try {
        Start-Process -FilePath $Path -WorkingDirectory (Split-Path $Path)
        return $true
    } catch {
        Write-Status "не получилось запустить Потассиум: $($_.Exception.Message)" Red
        return $false
    }
}

# Перед первым запуском окон открываем Потассиум: окно, открытое без него, остаётся без автоэкзекута. -NoWait: не ждать
# его загрузки здесь (ждёт Wait-PotassiumWarmup перед первым окном) — при старте он грузится, пока ждём интернет.
function Initialize-Potassium([switch]$NoWait) {
    if ($NoPotassium) { return }
    if ($Executor -ne 'potassium') { Initialize-Real -NoWait:$NoWait; return }
    $path = Get-PotassiumExe
    if (Get-Process -Name Potassium -ErrorAction SilentlyContinue) { Write-Status 'Потассиум уже открыт.' Green; return }
    if (-not $path) {
        Write-Status 'Не нашёл Potassium.exe — открой Потассиум сам или укажи путь к нему в настройках.' DarkYellow
        return
    }
    Write-Status "открываю Потассиум ($path), даю ему $PotassiumWarmup сек загрузиться..." Cyan
    if (-not (Start-Potassium $path)) { return }
    $script:PotassiumStartedAt = Get-Date
    if ($NoWait) { return }
    Start-Sleep -Seconds $PotassiumWarmup
    if (Get-Process -Name Potassium -ErrorAction SilentlyContinue) { Write-Status 'Потассиум открыт.' Green }
    else { Write-Status 'Потассиум закрылся сразу после запуска — открой его сам, окна пока запускаю без него.' Red }
}

# Потассиум открыт этим движком только что: дожидаемся, пока пройдут его $PotassiumWarmup сек (окно Роблокса без него
# осталось бы без скрипта). Обычно они уже прошли, пока ждали интернет и проверяли куку.
function Wait-PotassiumWarmup {
    if (-not $script:PotassiumStartedAt) { return }
    $left = $PotassiumWarmup - ((Get-Date) - $script:PotassiumStartedAt).TotalSeconds
    if ($left -gt 0) { Start-Sleep -Milliseconds ([int]($left * 1000)) }
    $script:PotassiumStartedAt = $null
    if (Get-Process -Name Potassium -ErrorAction SilentlyContinue) { Write-Status 'Потассиум открыт.' Green }
    else { Write-Status 'Потассиум закрылся сразу после запуска — открой его сам, окна пока запускаю без него.' Red }
}

# Потассиум закрылся — открываем снова (не чаще раза в минуту): без него автоаттач не работает и новые окна
# остаются без скрипта. Скрипт перестал работать во всех окнах сразу — значит, сломался сам Потассиум (так было
# после нехватки памяти ночью 03.10): перезапускаем его не чаще раза в 20 мин, а окна без скрипта перезапустит Step-Account.
function Restart-PotassiumIfDead {
    if ($NoPotassium) { return }
    if ($Executor -ne 'potassium') { Restart-RealIfDead; return }
    $now = Get-Date
    $running = @(Get-Process -Name Potassium -ErrorAction SilentlyContinue)
    if (-not $running) {
        if (($now - $script:LastPotassiumRestart).TotalSeconds -lt 60) { return }
        $path = Get-PotassiumExe
        if (-not $path) { return }
        $script:LastPotassiumRestart = $now
        Write-Status 'Потассиум закрыт — открываю: без него автоаттач не работает и новые окна остаются без скрипта.' Yellow
        if (Start-Potassium $path) {
            Start-Sleep -Seconds $PotassiumWarmup
            $null = Confirm-PotassiumAutoAttachOn -Now
        }
        return
    }
    if (($now - $script:LastPotassiumRestart).TotalMinutes -lt 20) { return }

    $withScript = @($script:Accounts | Where-Object { $_.Tracker -and $_.Tracker.ScriptSeen })
    if ($withScript.Count -lt [Math]::Min(2, $script:Accounts.Count)) { return }
    if (@($withScript | Where-Object { -not (Test-ScriptMissing $_) }).Count) { return }

    $path = Get-PotassiumExe
    if (-not $path) { return }
    $script:LastPotassiumRestart = $now
    Write-Status 'Скрипт перестал работать во всех окнах сразу — перезапускаю Потассиум.' Yellow
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    if (Start-Potassium $path) {
        Start-Sleep -Seconds 10
        $null = Confirm-PotassiumAutoAttachOn -Now
    }
}

#region Внешние экзекьюторы: REAL (Real Executor), Volt, Synapse Z
# Все три подключаются к окнам сами (автоинжект в их настройках) и выполняют скрипты из своей папки autoexec, а
# AutoRejoin их только открывает и проверяет. Отличаются они только именем, процессом, папкой и способом запуска
# (Set-ExecProfile). Ниже — как это устроено на примере REAL.
# REAL открывается с правами администратора (своё задание планировщика Real\Launch), поэтому его окно и кнопки обычной
# программе недоступны: Windows не даёт читать и нажимать элементы окна с правами выше. Поэтому:
#   - в окна подключается сам REAL — его Auto Inject (Settings -> Client), скрипты из Real\autoexec запускает Auto Execute;
#   - что REAL подключился к окну, видно по отметке: наш autorejoin_inject.lua в autoexec при инжекте пишет в workspace
#     autorejoin_inject_<UserId>.txt;
#   - что скрипт фермы запустился — по тем же файлам javad1khive_*, что и с Потассиумом, только в Real\workspace.
$RealMarkerName = 'autorejoin_inject.lua'
$script:LastRealStart = [datetime]::MinValue
$script:RealSettingsCheckAt = [datetime]::MinValue
$script:RealAutoExecWarned = $false
$script:RealNoInjectWarnedAt = [datetime]::MinValue
$script:RealMissingWarned = $false

$script:ExecResolveAt = [datetime]::MinValue
$script:ExecConfigured = $null

# Имя, процессы и папка выбранного экзекьютора.
function Set-ExecProfile {
    switch ($Executor) {
        'volt'     { $script:ExecName = 'Volt';      $script:ExecProcs = @('Volt*') }
        'synapsez' { $script:ExecName = 'Synapse Z'; $script:ExecProcs = @('*Synapse*') }
        default    { $script:ExecName = 'REAL';      $script:ExecProcs = @('Real') }
    }
    $script:ExecResolveAt = [datetime]::MinValue
    Resolve-ExecDir
}

# Папка Volt / Synapse Z: указанная в настройках, у запущенного процесса, прошлый раз (executor-<id>.path),
# обычные места (Рабочий стол, Загрузки, Документы, AppData, Program Files — на 2 уровня вглубь). Подходит папка,
# где есть workspace и exe с именем экзекьютора. У REAL папка всегда одна: %LOCALAPPDATA%\Real.
function Resolve-ExecDir {
    if ($Executor -eq 'real' -or $Executor -eq 'potassium') {
        $script:ExecDir = $RealDir
        $script:ExecExe = Join-Path $RealDir 'Update.exe'
        return
    }
    $now = Get-Date
    if (($now - $script:ExecResolveAt).TotalSeconds -lt 60) { return }
    $script:ExecResolveAt = $now
    $pattern = if ($Executor -eq 'volt') { '(?i)volt' } else { '(?i)synapse' }
    $pathFile = Join-Path $DataDir "executor-$Executor.path"
    $candidates = @()
    if ($ExecutorPath) { $candidates += $ExecutorPath }
    foreach ($process in @(Get-Process -Name $script:ExecProcs -ErrorAction SilentlyContinue)) { try { if ($process.Path) { $candidates += $process.Path } } catch {} }
    try { $candidates += (Get-Content -LiteralPath $pathFile -TotalCount 1 -Encoding UTF8 -ErrorAction Stop).Trim() } catch {}
    $roots = @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:USERPROFILE 'Downloads'), [Environment]::GetFolderPath('MyDocuments'),
        $env:LOCALAPPDATA, $env:APPDATA, $env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    foreach ($root in $roots) {
        foreach ($dir in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match $pattern })) {
            $candidates += $dir.FullName
            $candidates += @(Get-ChildItem -LiteralPath $dir.FullName -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
        }
    }
    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        $dir = if (Test-Path -LiteralPath $candidate -PathType Leaf) { Split-Path $candidate } else { $candidate }
        if (-not (Test-Path -LiteralPath (Join-Path $dir 'workspace'))) { continue }
        $exe = @(Get-ChildItem -LiteralPath $dir -Filter '*.exe' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.BaseName -match $pattern -and $_.BaseName -notmatch '(?i)unins|updat|setup|install' } |
            Sort-Object { $_.Name.Length }) | Select-Object -First 1
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $exe = Get-Item -LiteralPath $candidate }
        $script:ExecDir = $dir
        $script:ExecExe = if ($exe) { $exe.FullName } else { $null }
        try { Set-Content -LiteralPath $pathFile -Value $dir -Encoding UTF8 } catch {}
        return
    }
    $script:ExecDir = if ($ExecutorPath) { $ExecutorPath } else { Join-Path $env:LOCALAPPDATA $script:ExecName }
    $script:ExecExe = $null
}

function Test-RealInstalled {
    if ($Executor -eq 'real') { return (Test-Path -LiteralPath (Join-Path $RealDir 'Update.exe')) }
    Resolve-ExecDir
    return (Test-Path -LiteralPath (Join-Path $script:ExecDir 'workspace'))
}
function Test-RealRunning { return [bool](Get-Process -Name $script:ExecProcs -ErrorAction SilentlyContinue) }

# Папка автоэкзекута: у экзекьюторов она называется по-разному.
function Get-ExecAutoexecDir {
    foreach ($name in 'autoexec', 'autoexecute', 'auto-execute', 'auto execute') {
        $dir = Join-Path $script:ExecDir $name
        if (Test-Path -LiteralPath $dir) { return $dir }
    }
    return $null
}

# Как его открывает ярлык: задание Real\Launch (с правами администратора, без окна UAC). Задания нет — Update.exe
# (тогда Windows может спросить разрешение).
function Start-Real {
    if ($Executor -eq 'real') {
        $null = & schtasks.exe /run /tn 'Real\Launch' 2>&1
        if ($LASTEXITCODE -eq 0) { return $true }
    }
    Resolve-ExecDir
    if (-not $script:ExecExe -or -not (Test-Path -LiteralPath $script:ExecExe)) {
        Write-Status "не нашёл exe $script:ExecName — открой его сам или укажи папку в настройках (Экзекьютор)." DarkYellow
        return $false
    }
    try {
        Start-Process -FilePath $script:ExecExe -WorkingDirectory (Split-Path $script:ExecExe) | Out-Null
        return $true
    } catch {
        Write-Status "не получилось открыть $($script:ExecName): $($_.Exception.Message)" Red
        return $false
    }
}

# Наш маленький скрипт в autoexec экзекьютора: при инжекте пишет отметку для AutoRejoin, а когда Роблокс показывает окно
# "Disconnected" (кик, бездействие, потеря связи, 256 "Lost connection due to an unknown error") — отметку вылета
# (Update-DisconnectMarks). Новый сетевой движок Роблокса (RbxTransport) строку "setting replicator disconnect reason"
# в лог не пишет, и окно с таким вылетом по логу выглядело "в игре" — 24.11 у игрока 256 так и не перезашёл.
# Только свой файл, остальное не трогаем.
function Install-RealInjectMarker {
    $dir = if ($Executor -eq 'potassium') {
        $own = Join-Path $env:LOCALAPPDATA 'Potassium\autoexec'
        if (Test-Path -LiteralPath $own) { $own } else { $null }
    } else { Get-ExecAutoexecDir }
    if (-not $dir) { return }
    $path = Join-Path $dir $RealMarkerName
    $text = @'
-- AutoRejoin: inject marker and disconnect watcher. When the executor injects, this writes autorejoin_inject_<UserId>.txt
-- to the workspace, so AutoRejoin knows the window got the executor. When Roblox shows the "Disconnected" prompt (kick,
-- idle, lost connection, error 256...), it writes autorejoin_disconnect_<UserId>.txt, so AutoRejoin rejoins right away.
-- Installed and updated by AutoRejoin; it only reads the game and changes nothing in it.
task.spawn(function()
	pcall(function()
		local Players = game:GetService("Players")
		local player = Players.LocalPlayer
		while not player do task.wait(0.5); player = Players.LocalPlayer end
		local uid = tostring(player.UserId)
		writefile("autorejoin_inject_" .. uid .. ".txt", tostring(os.time()) .. " " .. tostring(game.PlaceId))
		local GuiService = game:GetService("GuiService")
		local sent = false
		local function check(title)
			if sent then return end
			local code = 0
			pcall(function() code = GuiService:GetErrorCode().Value end)
			local message = ""
			pcall(function() message = tostring(GuiService:GetErrorMessage()) end)
			local byTitle = title ~= nil and string.find(string.lower(tostring(title)), "disconnect", 1, true) ~= nil
			if not ((code >= 256 and code < 512) or byTitle) then return end
			sent = true
			local text = tostring(os.time()) .. " " .. tostring(code) .. " " .. tostring(game.PlaceId) .. " " .. (string.gsub(message, "[\r\n]+", " "))
			pcall(writefile, "autorejoin_disconnect_" .. uid .. ".txt", text)
		end
		GuiService.ErrorMessageChanged:Connect(function()
			task.wait(0.2)
			check(nil)
		end)
		local overlay = game:GetService("CoreGui"):WaitForChild("RobloxPromptGui", 60):WaitForChild("promptOverlay", 60)
		overlay.ChildAdded:Connect(function(child)
			if child.Name ~= "ErrorPrompt" then return end
			task.wait(0.3)
			local title = nil
			pcall(function() title = child.TitleFrame.ErrorTitle.Text end)
			check(title)
		end)
	end)
end)
'@
    try {
        if (-not (Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path) -ne $text) {
            [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
        }
    } catch { Write-Status "не получилось положить отметку инжекта в autoexec $($script:ExecName): $($_.Exception.Message)" DarkYellow }
}

function Initialize-Real([switch]$NoWait) {
    if (-not (Test-RealInstalled)) {
        if (-not $script:RealMissingWarned) {
            $script:RealMissingWarned = $true
            $how = if ($Executor -eq 'real') { 'нет папки %LOCALAPPDATA%\Real' } else { 'не нашёл его папку с workspace — укажи её в настройках (Экзекьютор)' }
            Write-Status "$($script:ExecName) не найден ($how) — или выбери другой экзекьютор в настройках." Red
        }
        return
    }
    Install-RealInjectMarker
    if (Test-RealRunning) { Write-Status "$($script:ExecName) уже открыт." Green; return }
    Write-Status "открываю $($script:ExecName)..." Cyan
    $script:LastRealStart = Get-Date
    if (-not (Start-Real)) { return }
    if ($NoWait) { return }
    for ($i = 0; $i -lt 30 -and -not (Test-RealRunning); $i++) { Start-Sleep -Milliseconds 500 }
    if (Test-RealRunning) { Write-Status "$($script:ExecName) открыт." Green } else { Write-Status "$($script:ExecName) не открылся за 15 сек — открой его сам." DarkYellow }
}

# REAL закрылся — открываем снова, не чаще раза в минуту: без него новые окна останутся без скрипта.
function Restart-RealIfDead {
    if (Test-RealRunning) { return }
    $now = Get-Date
    if (($now - $script:LastRealStart).TotalSeconds -lt 60 -or -not (Test-RealInstalled)) { return }
    $script:LastRealStart = $now
    Install-RealInjectMarker
    Write-Status "$($script:ExecName) закрыт — открываю: без него новые окна останутся без скрипта." Yellow
    $null = Start-Real
}

# Настройки REAL (data\config\settings.json): Auto Execute должен быть включён — иначе скрипты из autoexec не запустятся.
# Auto Inject в файле появляется, только когда его переключили; есть и выключен — сразу предупреждаем, нет записи —
# судим по отметкам инжекта (Update-RealState).
function Get-RealSetting([string]$Name) {
    try { $json = [IO.File]::ReadAllText((Join-Path $RealDir 'data\config\settings.json')) } catch { return $null }
    $found = [regex]::Matches($json, '"' + $Name + '"\s*:\s*(true|false)')
    if ($found.Count) { return $found[$found.Count - 1].Groups[1].Value -eq 'true' }
    return $null
}

function Confirm-RealSettings([switch]$Now) {
    $time = Get-Date
    if (-not $Now -and ($time - $script:RealSettingsCheckAt).TotalSeconds -lt 60) { return $true }
    $script:RealSettingsCheckAt = $time
    Install-RealInjectMarker
    if ($Executor -ne 'real') {
        if (-not (Get-ExecAutoexecDir) -and -not $script:RealAutoExecWarned -and (Test-RealInstalled)) {
            $script:RealAutoExecWarned = $true
            Write-Status "у $($script:ExecName) не нашёл папку autoexec в $($script:ExecDir) — отметку инжекта положить некуда, проверка инжекта будет только по скрипту." DarkYellow
        }
        return $true
    }
    $ok = $true
    if ((Get-RealSetting 'autoExec') -eq $false) {
        $ok = $false
        if (-not $script:RealAutoExecWarned) {
            $script:RealAutoExecWarned = $true
            Write-Status 'В REAL выключен Auto Execute — скрипты из autoexec не запустятся. Включи: Settings -> Client -> Auto Execute.' Red
        }
    } else { $script:RealAutoExecWarned = $false }
    $inject = Get-RealSetting 'auto_?[Ii]nject(ion)?'
    if ($inject -eq $false) {
        $ok = $false
        if (-not $script:AutoAttachWarned) {
            $script:AutoAttachWarned = $true
            Write-Status 'В REAL выключен Auto Inject — он не подключится к окнам сам. Включи: Settings -> Client -> Auto Inject.' Red
        }
    } elseif ($inject -eq $true) { $script:AutoAttachWarned = $false }
    return $ok
}

# Каждый круг: отметки вылета от нашего скрипта в autoexec (Install-RealInjectMarker). Отметка новее последнего захода
# окна — Роблокс показал "Disconnected": окно уже не на сервере, перезаходим сразу (как по коду кика в логе).
function Update-DisconnectMarks {
    foreach ($account in $script:Accounts) {
        $tracker = $account.Tracker
        if (-not $tracker -or -not $account.UserId -or -not $tracker.InGame) { continue }
        $file = Join-Path $Workspace "autorejoin_disconnect_$($account.UserId).txt"
        $time = try { (Get-Item -LiteralPath $file -ErrorAction Stop).LastWriteTime } catch { $null }
        if (-not $time) { continue }
        $since = if ($tracker.LastJoinAt) { $tracker.LastJoinAt } else { $tracker.Start }
        if ($time -le $since) { continue }
        $text = try { [IO.File]::ReadAllText($file).Trim() } catch { '' }
        $parts = @($text -split ' ', 4)
        $code = 0
        if ($parts.Count -ge 2) { [void][int]::TryParse($parts[1], [ref]$code) }
        if ($code -lt 256 -or $code -ge 512) { $code = 256 }
        $message = if ($parts.Count -ge 4) { $parts[3].Trim() } else { '' }
        $tracker.KickCode = $code
        $tracker.KickAt = Get-Date
        $tracker.InGame = $false
        $tracker.StateSince = Get-Date
        $tracker.LastReason = $code
        $what = if ($message) { " — $message" } else { '' }
        Write-Status "Роблокс показал окно Disconnected (код $code$what) — перезахожу." Yellow $account
    }
}

# Каждый круг: отметки инжекта из workspace REAL. Окна, к которым REAL подключился после их запуска, — Attached.
# Если REAL не подключился ни к одному окну, хотя они в игре дольше 90 сек, — подсказка (не чаще раза в 10 минут).
function Update-RealState {
    $now = Get-Date
    $waiting = @()
    foreach ($account in $script:Accounts) {
        $tracker = $account.Tracker
        if (-not $tracker -or -not $account.UserId) { continue }
        if (-not $tracker.Attached) {
            $file = Join-Path $Workspace "autorejoin_inject_$($account.UserId).txt"
            $time = try { (Get-Item -LiteralPath $file -ErrorAction Stop).LastWriteTime } catch { $null }
            if ($time -and $time -ge $tracker.Start.AddSeconds(-2)) {
                Set-TrackerAttached $tracker
                $after = if ($tracker.JoinedAt) { ' ({0:N0} сек после захода)' -f [Math]::Max(0, ($time - $tracker.JoinedAt).TotalSeconds) } else { '' }
                Write-Status "$($script:ExecName) заинжектился в окно$after." DarkGray $account
            }
        }
        if (-not $tracker.Attached -and -not $tracker.ScriptSeen -and $tracker.InGame -and $tracker.JoinedAt -and ($now - $tracker.JoinedAt).TotalSeconds -ge 90) {
            $waiting += $account
        }
    }
    $anyInjected = @($script:Accounts | Where-Object { $_.Tracker -and ($_.Tracker.Attached -or $_.Tracker.ScriptSeen) }).Count
    if ($waiting.Count -and -not $anyInjected -and ($now - $script:RealNoInjectWarnedAt).TotalMinutes -ge 10) {
        $script:RealNoInjectWarnedAt = $now
        $what = if (-not (Test-RealRunning)) { "$($script:ExecName) закрыт — открываю его" }
            elseif ($Executor -eq 'real') { 'проверь, что в REAL включён Auto Inject (Settings -> Client) и есть ключ' }
            else { "проверь, что в $($script:ExecName) включён автоинжект (Auto Inject / Auto Attach) и он обновлён под текущий Роблокс" }
        Write-Status "$($script:ExecName) не заинжектился ни в одно окно — $what." Red
    }
}
#endregion

# Окно Потассиума через UI Automation: текст статуса справа внизу ("No client" или число клиентов), строки консоли
# (из них — "Client <PID> attached") и окно "Channel build mismatch". Потассиум — WebView2, его элементы видны как обычные.
# Chromium строит дерево доступности только после первого запроса, поэтому первые разы элементов может почти не быть.
function Read-PotassiumState {
    # Окно Потассиума ищем по классу "Tauri Window", а не через MainWindowHandle: когда Потассиум свёрнут, Windows
    # считает главным его служебное окно 16x16 ("Tao Thread Event Target"), и скрипт видел "пустой" Потассиум
    # (07–08.10 из-за этого не жался Attach и Потассиум зря перезапускался).
    $process = $null; $handle = [IntPtr]::Zero
    foreach ($candidate in @(Get-Process -Name Potassium -ErrorAction SilentlyContinue | Sort-Object { try { $_.StartTime } catch { [datetime]::MaxValue } })) {
        $handle = [RejoinWindows]::FindClassWindow($candidate.Id, 'Tauri Window')
        if ($handle -ne [IntPtr]::Zero) { $process = $candidate; break }
    }
    if (-not $process) { return $null }
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
    $all = [Windows.Automation.Condition]::TrueCondition
    $descendants = [Windows.Automation.TreeScope]::Descendants

    $elements = @()
    for ($try = 0; $try -lt 3; $try++) {
        $elements = @($window.FindAll($descendants, $all))
        if ($elements.Count -ge 10) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($elements.Count -lt 10) {
        return [pscustomobject]@{ Pid = $process.Id; Empty = $true; Status = $null; Clients = $null; Console = $null; LastLine = $null; LastAt = $null; AttachedPids = @(); FixButton = $null; SignIn = $false }
    }

    # Экран входа ("Sign in to continue."): Потассиум разлогинился и ни к одному окну не подключится, пока в него
    # не войдут (16.10 с 01:23 до обеда окна так простояли без скрипта: окно Потассиума живое, Fix it нет).
    $signIn = [bool]($elements | Where-Object { $_.Current.ClassName -eq 'auth' -or $_.Current.Name -eq 'Sign in to continue.' } | Select-Object -First 1)

    # Статус — последний текст в нижней строке (после имени файла, "UTF-8" и т.п.).
    $status = $null
    $statusBar = $elements | Where-Object { $_.Current.ClassName -eq 'status' } | Select-Object -First 1
    if ($statusBar) {
        $texts = @($statusBar.FindAll($descendants, (New-Object Windows.Automation.PropertyCondition(
            [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text))))
        if ($texts) { $status = $texts[-1].Current.Name }
    }
    $clients = if ($status -match '^\s*No client') { 0 } elseif ($status -match '(\d+)') { [int]$Matches[1] } else { $null }

    # Строка консоли идёт сразу за своим временем "HH:mm:ss". Свои строки Потассиума ("Client 6804 attached.")
    # отличаем от вывода скриптов, чтобы показать в логе понятную последнюю.
    $console = $null; $consoleAt = $null; $own = $null
    $attachedPids = @()
    $now = Get-Date
    for ($i = 0; $i -lt $elements.Count - 1; $i++) {
        $stamp = $elements[$i].Current.Name
        if ($stamp -notmatch '^\d\d:\d\d:\d\d$') { continue }
        $line = $elements[$i + 1].Current.Name
        $console = $line
        $consoleAt = $stamp
        if ($line -notmatch '^\[|^=+$') { $own = $line }
        if ($line -match '^Client (\d+) attached') {
            # Время строки нужно, чтобы не принять за своё подключение старую строку про другое окно с тем же PID:
            # Windows раздаёт номера процессов повторно, и к вечеру 08.10 так "подключёнными" считались окна без скрипта.
            $at = [datetime]::ParseExact($stamp, 'HH:mm:ss', $null)
            if ($at -gt $now.AddMinutes(1)) { $at = $at.AddDays(-1) }
            $attachedPids += [pscustomobject]@{ Pid = [int]$Matches[1]; At = $at }
        }
    }
    # Окно "Channel build mismatch": после обновления Роблокса Потассиум не поддерживает новую сборку, пока сам
    # не заменит её своей (кнопка "Fix it"). Пока это окно висит, Потассиум к окнам не подключается.
    $fixButton = $null
    if ($elements | Where-Object { $_.Current.Name -eq 'Channel build mismatch' }) {
        $fixButton = $elements | Where-Object {
            $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -eq 'Fix it'
        } | Select-Object -First 1
    }
    return [pscustomobject]@{
        Pid = $process.Id; Empty = $false; Status = $status; Clients = $clients
        Console = if ($own) { $own } else { $console }; LastLine = $console; LastAt = $consoleAt; AttachedPids = $attachedPids
        FixButton = $fixButton; SignIn = $signIn
    }
}

# Скрипт в окна подключает автоаттач Потассиума (Settings -> Client -> Auto attach): сам AutoRejoin Attach не жмёт
# (с 13.10 — двойная инъекция, автоаттач + наш Attach, роняла окна, как 11.10 в 22:59 и 23:02). Поэтому автоаттач
# должен быть включён. Значение читаем из localStorage Потассиума (leveldb: последняя запись "autoAttach":true/false).
$PotassiumStorage = Join-Path $env:LOCALAPPDATA 'com.pot.potassium-tauri\EBWebView\Default\Local Storage\leveldb'

function Get-PotassiumAutoAttach {
    $value = $null
    # .ldb — старые сжатые записи, .log — свежие, дописываются по порядку: читаем сначала .ldb, потом .log.
    $files = @(Get-ChildItem -LiteralPath $PotassiumStorage -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.ldb', '.log' } | Sort-Object { $_.Extension -eq '.log' }, LastWriteTime)
    foreach ($file in $files) {
        try {
            $stream = [IO.File]::Open($file.FullName, 'Open', 'Read', 'ReadWrite')
            try { $bytes = New-Object byte[] $stream.Length; $null = $stream.Read($bytes, 0, $bytes.Length) } finally { $stream.Close() }
        } catch { continue }
        $found = [regex]::Matches([Text.Encoding]::UTF8.GetString($bytes), '"autoAttach":(true|false)')
        if ($found.Count) { $value = $found[$found.Count - 1].Groups[1].Value -eq 'true' }
    }
    return $value
}

function Find-PotassiumElement($Root, [string]$Name, [string]$ClassName, $ControlType) {
    $conditions = @()
    if ($Name) { $conditions += New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $Name) }
    if ($ClassName) { $conditions += New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ClassNameProperty, $ClassName) }
    if ($ControlType) { $conditions += New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType) }
    $condition = if ($conditions.Count -eq 1) { $conditions[0] } else { New-Object Windows.Automation.AndCondition($conditions) }
    return $Root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

# Логин и пароль Потассиума для автовхода — из vault.dat (вписываются в настройках приложения). В лог не пишем.
function Get-PotassiumLogin {
    if (-not $PotassiumAutoLogin) { return $null }
    $saved = $script:VaultPotassium
    if (-not $saved -or -not ([string]$saved.user).Trim() -or -not [string]$saved.password) { return $null }
    [pscustomobject]@{ User = ([string]$saved.user).Trim(); Password = [string]$saved.password }
}

# Текст в поле окна Потассиума. Через UI Automation (Chromium выставляет значение и шлёт полю input/change, как при вводе);
# если поле так не меняется — фокус и ввод с клавиатуры.
function Set-PotassiumField($Element, [string]$Text) {
    try {
        $Element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
        return
    } catch {}
    Add-Type -AssemblyName System.Windows.Forms
    $Element.SetFocus()
    Start-Sleep -Milliseconds 150
    [Windows.Forms.SendKeys]::SendWait('^a')
    [Windows.Forms.SendKeys]::SendWait([regex]::Replace($Text, '[+^%~(){}\[\]]', '{$0}'))
}

# Экран входа Потассиума ("Sign in to continue."): вписывает логин и пароль и жмёт Sign in. $true — экран входа пропал
# за 20 сек; иначе строка с причиной (текст ошибки Потассиума, если он его показал).
function Invoke-PotassiumSignIn($Login) {
    $handle = [IntPtr]::Zero
    foreach ($candidate in @(Get-Process -Name Potassium -ErrorAction SilentlyContinue | Sort-Object { try { $_.StartTime } catch { [datetime]::MaxValue } })) {
        $handle = [RejoinWindows]::FindClassWindow($candidate.Id, 'Tauri Window')
        if ($handle -ne [IntPtr]::Zero) { break }
    }
    if ($handle -eq [IntPtr]::Zero) { return 'нет окна Потассиума' }
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
    $edit = [Windows.Automation.ControlType]::Edit
    $user = Find-PotassiumElement $window 'Username' $null $edit
    $pass = Find-PotassiumElement $window 'Password' $null $edit
    $button = Find-PotassiumElement $window 'Sign in' $null ([Windows.Automation.ControlType]::Button)
    if (-not $user -or -not $pass -or -not $button) { return 'не нашёл поля входа' }

    Set-PotassiumField $user $Login.User
    Set-PotassiumField $pass $Login.Password
    Start-Sleep -Milliseconds 300
    $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        $state = Read-PotassiumState
        if ($state -and -not $state.Empty -and -not $state.SignIn) { return $true }
    }
    # Сообщение под формой (неверный пароль и т.п.)
    $alerts = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ClassNameProperty, 'monaco-alert'))) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
    if ($alerts) { return "Потассиум ответил: $($alerts -join ' / ')" }
    return $null
}

# Включает (или выключает) автоаттач через окно настроек Потассиума: вкладка Start -> Settings -> Client -> Auto attach.
# Открытую до этого вкладку редактора возвращаем на место. Возвращает Ok — переключатель теперь в нужном положении,
# Changed — пришлось переключить.
function Set-PotassiumAutoAttach([bool]$On) {
    $result = [pscustomobject]@{ Ok = $false; Changed = $false }
    $wanted = if ($On) { [Windows.Automation.ToggleState]::On } else { [Windows.Automation.ToggleState]::Off }
    $handle = [IntPtr]::Zero
    foreach ($candidate in @(Get-Process -Name Potassium -ErrorAction SilentlyContinue | Sort-Object { try { $_.StartTime } catch { [datetime]::MaxValue } })) {
        $handle = [RejoinWindows]::FindClassWindow($candidate.Id, 'Tauri Window')
        if ($handle -ne [IntPtr]::Zero) { break }
    }
    if ($handle -eq [IntPtr]::Zero) { return $result }
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
    $tabs = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem))))
    $activeTab = $tabs | Where-Object { $_.Current.ClassName -match '\bis-active\b' } | Select-Object -First 1
    $startTab = $tabs | Where-Object { $_.Current.Name -eq 'Start' } | Select-Object -First 1

    $settings = Find-PotassiumElement $window 'Settings' 'link-row'
    if (-not $settings -and $startTab) {
        $startTab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 600
        $settings = Find-PotassiumElement $window 'Settings' 'link-row'
    }
    if (-not $settings) { return $result }
    $settings.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()

    $dialog = $null
    for ($try = 0; $try -lt 10 -and -not $dialog; $try++) { Start-Sleep -Milliseconds 300; $dialog = Find-PotassiumElement $window $null 'settings-dialog' }
    if ($dialog) {
        $client = Find-PotassiumElement $dialog 'Client' 'rail-row'
        if ($client) { $client.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500 }
        $switch = @($dialog.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition(
            [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::CheckBox)))) |
            Where-Object { $_.Current.Name -like 'Auto attach*' } | Select-Object -First 1
        if ($switch) {
            $toggle = $switch.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
            if ($toggle.Current.ToggleState -ne $wanted) { $toggle.Toggle(); $result.Changed = $true; Start-Sleep -Milliseconds 300 }
            $result.Ok = $toggle.Current.ToggleState -eq $wanted
        }
        $close = Find-PotassiumElement $dialog 'Close settings'
        if ($close) { $close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    }
    if ($activeTab -and $activeTab.Current.Name -ne 'Start') {
        try { $activeTab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch {}
    }
    return $result
}

# Раз в минуту: автоаттач выключен (Потассиум сбросил настройки и т.п.) — включаем. Если в настройках Потассиума
# записи ещё нет (его не трогали), с -Now смотрим сам переключатель в окне настроек; без -Now окно настроек
# каждую минуту не открываем. Возвращает $true, если автоаттач включён.
function Confirm-PotassiumAutoAttachOn([switch]$Now) {
    if ($NoPotassium) { return $true }
    if ($Executor -ne 'potassium') { return (Confirm-RealSettings -Now:$Now) }
    if (-not $KeepAutoAttach) { return $true }
    $time = Get-Date
    if (-not $Now -and ($time - $script:AutoAttachCheckAt).TotalSeconds -lt 60) { return $true }
    $script:AutoAttachCheckAt = $time
    if (-not (Get-Process -Name Potassium -ErrorAction SilentlyContinue)) { return $false }
    $stored = Get-PotassiumAutoAttach
    if ($stored -eq $true -or ($null -eq $stored -and -not $Now)) { return $true }

    $set = Set-PotassiumAutoAttach $true
    if ($set.Ok) {
        $script:AutoAttachFails = 0
        if ($set.Changed) { Write-Status 'В Потассиуме был выключен автоаттач — включил: без него скрипт в окна не попадает.' Yellow }
        return $true
    }
    # Первые неудачи бывают, пока Потассиум только открылся и ещё рисует окно.
    if (++$script:AutoAttachFails -ge 3 -and -not $script:AutoAttachWarned) {
        $script:AutoAttachWarned = $true
        Write-Status 'В Потассиуме выключен автоаттач, а включить его сам не смог. Включи: Settings -> Client -> Auto attach.' Red
    }
    return $false
}

# Потассиум показал "Channel build mismatch": сборка клиента Роблокса на диске не та, которую он поддерживает (вышел новый
# Роблокс или обновился сам Потассиум — 12.11 он обновился при старте после перезагрузки). "Fix it" ставит в папку клиента
# его сборку, но только если файлы клиента никто не держит, а сам Потассиум закрывает лишь окно, к которому подключался:
#   - с одним открытым окном "Fix it" проходил (17:25, 23:42: "Client updated to version-...");
#   - 10.11 в 22:20 открытых окон было два: "could not write ...RobloxPlayerBeta.exe: The process cannot access the file
#     because it is being used by another process", окно "Fix it" пропало, клиент остался прежним, и 12.11 ферма стояла;
#   - когда AutoRejoin закрыл заранее все окна (08.10 в 16:09), Потассиум не смог закрыть "своё": "could not close the
#     Roblox client: Access is denied".
# Поэтому 1-я попытка: закрываем весь Роблокс, кроме окна, к которому шёл аттач (Get-FixKeepPid), ждём, пока процессы правда
# исчезнут, и жмём "Fix it". 2-я (если окно "Fix it" ещё висит): закрываем вообще всё и жмём снова. Ответ ждём до 5 минут
# (он скачивает ~150 МБ). Не вышло — окна перезайдут, наш Attach (Invoke-ManualAttach) снова вызовет "Channel build
# mismatch", и всё повторится; 3 неудачи за час — плашка, и полчаса не повторяем.
function Invoke-PotassiumClientFix($State) {
    if (Test-FixSuspended) { return }
    Write-Status 'Потассиум не поддерживает сборку Роблокса на диске ("Channel build mismatch") — закрываю окна Роблокса (с открытым Роблоксом он не может заменить клиент) и жму "Fix it". Окна перезайдут.' Yellow
    $result = $null
    for ($round = 1; $round -le 2; $round++) {
        $keep = if ($round -eq 1) { Get-FixKeepPid } else { 0 }
        $left = @(Close-RobloxForFix $keep)
        if ($left) { Write-Status "не закрылись за 20 сек: $($left -join ', ') — всё равно жму Fix it." DarkYellow }
        $State = Read-PotassiumState
        if (-not $State -or -not $State.FixButton) { $result = 'окно "Fix it" пропало'; break }
        $what = if ($keep) { "открыто только окно, к которому подключался Потассиум (PID $keep), — его он закроет сам" } else { 'все окна Роблокса закрыты' }
        Write-Status "жму Fix it: $what." DarkGray
        $pressedAt = Get-Date
        try { $State.FixButton.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
        catch { $result = "не нажалась кнопка Fix it: $($_.Exception.Message)"; break }
        $result = Wait-PotassiumFixResult $pressedAt
        if ($result -match '^Client updated' -or $script:StopRequested) { break }
        if ($round -eq 1) { Write-Status "Потассиум не смог ($result) — закрываю весь Роблокс и жму ещё раз." DarkYellow }
    }
    if ($result -match '^Client updated') {
        Write-Status "Потассиум обновил клиент: $result" Green
        $script:FixFails = @()
    } elseif (-not $script:StopRequested) {
        Register-FixFailure $result
    }
    # Окна закрыты (Потассиумом или нами) — пусть перезайдут обычным порядком, мейн первым. Закрытые процессы, пока они
    # не исчезли, за окна аккаунтов не принимаем, их логи другим окнам не отдаём.
    foreach ($tracker in @($script:Trackers.Values)) {
        $tracker.Alive = $false
        Add-UsedLog $tracker
        $script:KilledPids[$tracker.Pid] = $tracker.Start
    }
    $script:Trackers.Clear()
    foreach ($account in $script:Accounts) {
        $account.Tracker = $null
        $account.LaunchedAt = $null
        $account.NextLaunch = (Get-Date).AddSeconds(5)
    }
    foreach ($process in @(Get-RobloxFixProcesses | Where-Object { $_.Name -eq "$PlayerProcess.exe" })) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

# Процессы, которые держат файлы клиента Роблокса: окна, их RobloxCrashHandler и запуски через Froststrap/Bloxstrap.
function Get-RobloxFixProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name='$PlayerProcess.exe' OR Name='RobloxCrashHandler.exe' OR Name='Froststrap.exe' OR Name='Bloxstrap.exe' OR Name='Fishstrap.exe'" -ErrorAction SilentlyContinue |
        ForEach-Object { [pscustomobject]@{ Id = [int]$_.ProcessId; Name = [string]$_.Name; ParentId = [int]$_.ParentProcessId } })
}

# Закрывает Роблокс, кроме окна $KeepPid и его RobloxCrashHandler, и ждёт до 20 сек, пока процессы правда исчезнут:
# закрывающийся процесс ещё держит файлы клиента. Возвращает то, что так и не закрылось.
function Close-RobloxForFix([int]$KeepPid = 0) {
    $deadline = (Get-Date).AddSeconds(20)
    while ($true) {
        $alive = @(Get-RobloxFixProcesses | Where-Object { -not $KeepPid -or ($_.Id -ne $KeepPid -and $_.ParentId -ne $KeepPid) })
        if (-not $alive) { break }
        if ((Get-Date) -ge $deadline) { return @($alive | ForEach-Object { "$($_.Name) (PID $($_.Id))" }) }
        foreach ($process in $alive) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 500
    }
    Start-Sleep -Milliseconds 1000   # файлы закрытого процесса Windows отпускает не мгновенно
    return @()
}

# Окно, к которому подключался Потассиум, когда показал "Channel build mismatch": то, где мы жали Attach, иначе самое новое
# окно без скрипта (окна запускаются по одному, автоаттач подключается к новому). 0 — такого нет.
function Get-FixKeepPid {
    $trackers = @($script:Trackers.Values | Where-Object { $_.Alive -and -not $_.IsTray })
    $pick = @($trackers | Where-Object { $_.ManualAttach -eq 'pressed' } | Sort-Object Start -Descending) +
        @($trackers | Where-Object { -not $_.ScriptSeen } | Sort-Object Start -Descending)
    if ($pick.Count) { return [int]$pick[0].Pid }
    return 0
}

# Ответ Потассиума на "Fix it": строка консоли "Client updated ..." или "Couldn't fix ...", написанная после нажатия (та же
# строка могла остаться в консоли от прошлого раза). Окно, которое Потассиум сейчас закрывает сам, не трогаем; команды
# приложения и состояние для него идут и пока ждём.
function Wait-PotassiumFixResult([datetime]$PressedAt) {
    $deadline = $PressedAt.AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        if (Wait-Interruptible 2) { return 'AutoRejoin остановили' }
        $state = Read-PotassiumState
        if (-not $state -or $state.LastLine -notmatch "^Client updated|^Couldn't fix") { continue }
        if ($state.LastAt) {
            $at = [datetime]::ParseExact($state.LastAt, 'HH:mm:ss', $null)
            if ($at -gt (Get-Date).AddMinutes(1)) { $at = $at.AddDays(-1) }
            if ($at -lt $PressedAt.AddSeconds(-2)) { continue }
        }
        return $state.LastLine
    }
    return 'Потассиум не ответил за 5 минут'
}

function Test-FixSuspended { return [bool]($script:FixSuspendedUntil -and (Get-Date) -lt $script:FixSuspendedUntil) }

# "Fix it" не помог. Окна перезайдут, и наш Attach снова вызовет "Channel build mismatch" — новая попытка. Три неудачи за час
# подряд — дело не в открытых окнах: плашка, и полчаса не чиним и Attach сами не жмём (не закрывать окна по кругу).
function Register-FixFailure([string]$Result) {
    $now = Get-Date
    $script:FixFails = @(@($script:FixFails) + $now | Where-Object { ($now - $_).TotalMinutes -lt 60 })
    if ($script:FixFails.Count -ge 3) {
        $script:FixFails = @()
        $script:FixSuspendedUntil = $now.AddMinutes(30)
        Show-Alert "Потассиум 3 раза за час не смог обновить клиент Роблокса ($Result). Закрой все окна Роблокса, кроме одного, нажми в Потассиуме Attach, потом Fix it. Сам попробую снова в $($script:FixSuspendedUntil.ToString('HH:mm'))."
    } else {
        Write-Status "Потассиум не обновил клиент ($Result) — окна перезайдут, и наш Attach снова вызовет ""Channel build mismatch"" (попытка $($script:FixFails.Count) из 3 за час)." DarkYellow
    }
}

# Окно Потассиума — раз в 3 сек, пока какое-то окно ждёт скрипт, иначе раз в 15 сек:
#   строки "Client <PID> attached" — к каким окнам подключился его автоаттач (для лога и причины перезапуска);
#   окно "Channel build mismatch" — жмём "Fix it";
#   пустое окно (интерфейс упал, процесс жив — 08.10 так окна часами стояли без скрипта) — перезапускаем Потассиум.
function Update-PotassiumState {
    if ($NoPotassium) { return }
    if ($Executor -ne 'potassium') { Update-RealState; return }
    $now = Get-Date
    $waiting = $script:Accounts | Where-Object { $_.Tracker -and -not $_.Tracker.ScriptSeen }
    $interval = if ($waiting) { 3 } else { 15 }
    if (($now - $script:PotassiumCheckAt).TotalSeconds -lt $interval) { return }
    $script:PotassiumCheckAt = $now

    # Основной процесс — самый старый с окном: при подключении Потассиум ненадолго запускает инжектор
    # bin\Potassium.exe с тем же именем процесса.
    $potassium = Get-Process -Name Potassium -ErrorAction SilentlyContinue |
        Sort-Object @{ Expression = { $_.MainWindowHandle -eq [IntPtr]::Zero } }, @{ Expression = { try { $_.StartTime } catch { [datetime]::MaxValue } } } |
        Select-Object -First 1
    if (-not $potassium) { $script:PotassiumUiMissingSince = $null; return }
    if ($script:PotassiumPid -ne $potassium.Id) {
        # Потассиум перезапустился — его прежние строки "attached" к окнам больше не относятся.
        $script:PotassiumPid = $potassium.Id
        $script:AttachedPids = @{}
    }

    $state = Read-PotassiumState
    if (-not $state -or $state.Empty) {
        if (-not $script:PotassiumUiMissingSince) { $script:PotassiumUiMissingSince = $now }
        $missing = ($now - $script:PotassiumUiMissingSince).TotalSeconds
        # Первые 15 сек молчим: сразу после запуска Потассиум ещё рисует своё окно.
        if ($missing -ge 15 -and -not $script:PotassiumUiWarned) {
            $script:PotassiumUiWarned = $true
            Write-Status 'Окно Потассиума пустое (ещё грузится или интерфейс упал). Не оживёт за 1.5 мин — перезапущу Потассиум.' DarkYellow
        }
        if ($missing -ge 90) {
            $script:PotassiumUiMissingSince = $null
            $script:PotassiumUiWarned = $false
            Write-Status 'Окно Потассиума пустое уже 1.5 мин — перезапускаю Потассиум.' Yellow
            Get-Process -Name Potassium, Decompiler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            $path = Get-PotassiumExe
            if ($path -and (Start-Potassium $path)) {
                Start-Sleep -Seconds $PotassiumWarmup
                $null = Confirm-PotassiumAutoAttachOn -Now
            }
        }
        return
    }
    $script:PotassiumUiMissingSince = $null
    $script:PotassiumUiWarned = $false
    $script:PotassiumClients = $state.Clients

    # Разлогинился: входим сами логином и паролем из настроек приложения (до 3 попыток, раз в минуту). Автовход выключен
    # или не вышло — плашка в приложении и напоминание раз в 30 мин. Окна без скрипта пока не перезапускаем (толку нет).
    if ($state.SignIn) {
        if (-not $script:PotassiumSignedOutSince) {
            $script:PotassiumSignedOutSince = $now
            $script:PotassiumSignInTries = 0
            $script:PotassiumSignInAlerted = $false
        }
        $login = Get-PotassiumLogin
        if ($login -and $script:PotassiumSignInTries -lt 3) {
            if (($now - $script:PotassiumSignInAt).TotalSeconds -ge 60) {
                $script:PotassiumSignInTries++
                $script:PotassiumSignInAt = $now
                Write-Status "Потассиум на экране входа — вхожу сам (логин и пароль из настроек, попытка $($script:PotassiumSignInTries) из 3)." Yellow
                $result = $null
                try { $result = Invoke-PotassiumSignIn $login } catch { $result = $_.Exception.Message }
                if ($result -eq $true) { Write-Status 'Вошёл в Потассиум.' Green }
                else { Write-Status "не вошёл: $(if ($result) { $result } else { 'экран входа не пропал за 20 сек' })." DarkYellow }
            }
            return
        }
        if (-not $script:PotassiumSignInAlerted) {
            $script:PotassiumSignInAlerted = $true
            Show-Alert $(if ($login) { 'Потассиум разлогинился, и сам войти я не смог (3 попытки) — войди в него сам, иначе скрипт не попадает ни в одно окно.' }
                else { 'Потассиум разлогинился — войди в него (Username / Password) или включи «Автовход» в настройках (Потассиум) и впиши там логин и пароль, иначе скрипт не попадает ни в одно окно.' })
        }
        if (($now - $script:PotassiumSignInWarnedAt).TotalMinutes -ge 30) {
            $script:PotassiumSignInWarnedAt = $now
            Write-Status ('Потассиум разлогинился (экран Sign in, с {0:HH:mm}) — войди в него, без этого скрипт ни в одно окно не попадёт. Окна без скрипта пока не перезапускаю.' -f $script:PotassiumSignedOutSince) Red
        }
        return
    }
    if ($script:PotassiumSignedOutSince) {
        $script:PotassiumSignedOutSince = $null
        $script:PotassiumSignInWarnedAt = [datetime]::MinValue
        $script:PotassiumSignInTries = 0
        $script:PotassiumSignInAt = [datetime]::MinValue
        Write-Status 'В Потассиум вошли — окна без скрипта сейчас перезайдут.' Green
        foreach ($account in $script:Accounts) { $account.ScriptCheckPausedUntil = $null; $account.NoScript = 0 }
    }

    if ($state.FixButton) { Invoke-PotassiumClientFix $state; return }

    $before = @($script:Accounts | Where-Object { $_.Tracker -and $_.Tracker.Attached } | ForEach-Object { $_.Name })
    Update-AttachedPids $state
    $new = @($script:Accounts | Where-Object {
        $_.Tracker -and $_.Tracker.Attached -and -not $_.Tracker.ScriptSeen -and $before -notcontains $_.Name
    })
    $manual = @($new | Where-Object { $_.Tracker.ManualAttach -eq 'pressed' })
    $auto = @($new | Where-Object { $_.Tracker.ManualAttach -ne 'pressed' })
    if ($auto) { Write-Status "Потассиум подключился (автоаттач): $(($auto | ForEach-Object { $_.Name }) -join ', ')." DarkGray }
    if ($manual) { Write-Status "Потассиум подключился после нашего Attach: $(($manual | ForEach-Object { $_.Name }) -join ', ')." Green }
}

# Отмечает окна, к которым Потассиум уже подключился: строка "Client <PID> attached" должна быть не раньше запуска
# самого окна — иначе это строка про старое окно, номер процесса которого Windows отдала новому.
function Update-AttachedPids($State) {
    foreach ($entry in $State.AttachedPids) {
        if (-not $script:AttachedPids.ContainsKey($entry.Pid) -or $script:AttachedPids[$entry.Pid] -lt $entry.At) {
            $script:AttachedPids[$entry.Pid] = $entry.At
        }
    }
    foreach ($tracker in $script:Trackers.Values) {
        $at = $script:AttachedPids[$tracker.Pid]
        if ($at -and $at -ge $tracker.Start.AddSeconds(-2)) { Set-TrackerAttached $tracker }
    }
}

function Set-TrackerAttached($Tracker) {
    if (-not $Tracker.Attached) { $Tracker.Attached = $true; $Tracker.AttachedAt = Get-Date }
}

# Автоаттач Потассиума не подключился к окну — жмём Attach сами. 12.11 после перезагрузки Потассиум обновился, и его
# автоаттач 3 часа молча не подключался к окнам, не показав и "Channel build mismatch"; на Attach же Потассиум отвечает
# либо подключением (скрипт запустится), либо окном "Channel build mismatch" — его чинит Invoke-PotassiumClientFix.
# Жмём осторожно: две инъекции в одно окно его роняют (11.10 в 22:59 и 23:02 — автоаттач + наш Attach), а после падения
# окна с Потассиумом новые окна умирают до перезагрузки ПК. Поэтому — раз на окно, через $ManualAttachDelay сек после захода
# (скрипт обычно запускается через 6–12 сек после захода, 99% — не позже 15: замер по 902 запускам), и только если:
#   - Потассиум так и не написал про это окно "Client <PID> attached" и скрипт в нём не запустился;
#   - Потассиум не подключается к чему-то прямо сейчас (его инжектор — второй процесс Potassium — не запущен);
#   - ни одно окно не грузится и не запускается (07.10 подключение к грузящемуся окну уронило его);
#   - остальные окна подключены этим же запуском Потассиума: Attach подключает все окна разом, уже подключённые им самим
#     пропускает, а подключённые прошлым его запуском — нет (07.10 в 20:16 так упали 3 окна из 4).
function Invoke-ManualAttach {
    if ($Executor -ne 'potassium') { return }   # окно REAL открыто с правами администратора — его кнопки нам недоступны
    $found = Get-ManualAttachTarget
    if (-not $found) { return }
    $account = $found.Account
    $tracker = $account.Tracker
    if ($found.Block) {
        if (-not $found.Quiet -and $tracker.ManualAttachNote -ne $found.Block) {
            $tracker.ManualAttachNote = $found.Block
            Write-Status "автоаттач Потассиума к окну не подключился, но Attach сам не жму: $($found.Block). Окно перезайдёт по проверке скрипта." DarkYellow $account
        }
        return
    }
    # Перед нажатием — всё свежее: скрипт мог запуститься, Потассиум — подключиться или показать "Channel build mismatch".
    $script:WorkspaceScanAt = [datetime]::MinValue
    Update-ScriptState $account
    if ($tracker.ScriptSeen -or $tracker.ForceReason) { return }
    $state = Read-PotassiumState
    if (-not $state -or $state.Empty -or $state.SignIn) { return }
    if ($state.FixButton) { Invoke-PotassiumClientFix $state; return }
    Update-AttachedPids $state
    if ($tracker.Attached) { return }

    $script:ManualAttachAt = Get-Date
    $after = ((Get-Date) - $tracker.JoinedAt).TotalSeconds
    $button = Find-PotassiumAttachButton
    if (-not $button) {
        $tracker.ManualAttach = 'nobutton'
        $seen = if ($script:PotassiumButtons) { " (кнопки в его окне: $($script:PotassiumButtons))" } else { '' }
        Write-Status ("автоаттач Потассиума не подключился за {0:N0} сек после захода, а кнопку Attach в окне Потассиума не нашёл{1} — нажми Attach сам." -f $after, $seen) DarkYellow $account
        return
    }
    $tracker.ManualAttach = 'pressed'
    Write-Status ('автоаттач Потассиума не подключился за {0:N0} сек после захода — жму Attach сам: скрипт запустится, или Потассиум покажет ошибку версии (её починю).' -f $after) Yellow $account
    try { $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch { Write-Status "не получилось нажать Attach: $($_.Exception.Message)" Red $account; return }
    $script:PotassiumCheckAt = [datetime]::MinValue   # окно Потассиума — на следующем круге: подключился или "Channel build mismatch"
}

# Через сколько сек после захода жать Attach: не позже, чем за треть $ScriptStartTimeout до перезапуска окна без скрипта.
function Get-ManualAttachDelay { return [Math]::Min($ManualAttachDelay, [Math]::Round($ScriptStartTimeout * 2 / 3)) }

# Окно в игре дольше этой задержки, а скрипта нет и Потассиум про него не писал "attached".
function Test-WaitingAttach($Tracker, [datetime]$Now) {
    return [bool]($Tracker -and $Tracker.InGame -and $Tracker.JoinedAt -and -not $Tracker.ScriptSeen -and -not $Tracker.Attached -and
        ($Now - $Tracker.JoinedAt).TotalSeconds -ge (Get-ManualAttachDelay))
}

# Подключено ли окно нынешним запуском Потассиума: он написал про окно "Client <PID> attached" или скрипт в окне запустился
# уже после старта этого Потассиума (строки консоли могли уйти — Потассиум хранит 500 последних).
function Test-AttachedByPotassium($Tracker, $Owner, $Potassium) {
    if ($script:PotassiumPid -eq $Potassium.Pid) {
        $at = $script:AttachedPids[$Tracker.Pid]
        if ($at -and $at -ge $Tracker.Start.AddSeconds(-2)) { return $true }
    }
    if ($Owner -and $Tracker.ScriptSeen) {
        $started = Get-ScriptFileTime 'started' $Owner
        if ($started -and $started -gt $Potassium.Start) { return $true }
    }
    return $false
}

# Основной процесс Потассиума (самый старый с окном) и подключается ли он сейчас к окну: на время подключения он
# запускает инжектор bin\Potassium.exe с тем же именем процесса (живёт несколько секунд).
function Get-PotassiumMain {
    $all = @(Get-Process -Name Potassium -ErrorAction SilentlyContinue |
        Sort-Object @{ Expression = { $_.MainWindowHandle -eq [IntPtr]::Zero } }, @{ Expression = { try { $_.StartTime } catch { [datetime]::MaxValue } } })
    if (-not $all) { return $null }
    $now = Get-Date
    $busy = [bool](@($all | Select-Object -Skip 1) | Where-Object { try { ($now - $_.StartTime).TotalSeconds -lt 30 } catch { $true } })
    $start = try { $all[0].StartTime } catch { $now }
    return [pscustomobject]@{ Pid = $all[0].Id; Start = $start; Busy = $busy }
}

# Кому сейчас жать Attach. $null — некому; иначе Account и Block — почему жать пока нельзя ('' — можно); Quiet — причина
# временная (окно грузится, Потассиум занят), в журнал о ней не пишем.
function Get-ManualAttachTarget {
    if ($ManualAttachDelay -le 0 -or $NoPotassium -or $ScriptStartTimeout -le 0) { return $null }
    if ($script:PotassiumSignedOutSince -or (Test-FixSuspended)) { return $null }
    $now = Get-Date
    if (($now - $script:ManualAttachAt).TotalSeconds -lt 30) { return $null }
    $target = $null
    foreach ($account in (Get-AccountsMainFirst)) {
        $tracker = $account.Tracker
        if (-not $tracker -or $tracker.ForceReason -or $tracker.ManualAttach -or $tracker.Adopted -or $tracker.UserOpen) { continue }
        if (Test-WaitingAttach $tracker $now) { $target = $account; break }
    }
    if (-not $target) { return $null }

    $result = [pscustomobject]@{ Account = $target; Block = ''; Quiet = $true }
    $potassium = Get-PotassiumMain
    if (-not $potassium) { $result.Block = 'Потассиум не запущен'; return $result }
    if ($potassium.Busy) { $result.Block = 'Потассиум прямо сейчас подключается'; return $result }
    if ($script:Accounts | Where-Object { $_.LaunchedAt }) { $result.Block = 'запускается другое окно'; return $result }
    foreach ($other in @($script:Trackers.Values | Sort-Object Start)) {
        if ($other.Pid -eq $target.Tracker.Pid -or $other.IsTray -or $other.ForceReason) { continue }
        $owner = Get-Owner $other
        $name = if ($owner) { $owner.Name } else { "PID $($other.Pid)" }
        if (-not $other.EverJoined) { $result.Block = "окно $name ещё грузится"; return $result }
        if (Test-AttachedByPotassium $other $owner $potassium) { continue }
        $result.Quiet = $false
        if ($other.ScriptSeen -or $other.Attached) { $result.Block = "окно $name подключено прошлым запуском Потассиума, повторный Attach его уронит"; return $result }
        if ($other.Adopted -or $other.UserOpen -or -not $owner) { $result.Block = "Attach подключил бы и окно $name (оно без скрипта и открыто не AutoRejoin)"; return $result }
        $result.Quiet = $true
        if (-not (Test-WaitingAttach $other $now)) { $result.Block = "окно $name само ещё ждёт автоаттача"; return $result }
        # Этому окну Attach тоже нужен — подключится тем же нажатием.
    }
    return $result
}

function Get-PotassiumWindowElement {
    foreach ($candidate in @(Get-Process -Name Potassium -ErrorAction SilentlyContinue | Sort-Object { try { $_.StartTime } catch { [datetime]::MaxValue } })) {
        $handle = [RejoinWindows]::FindClassWindow($candidate.Id, 'Tauri Window')
        if ($handle -ne [IntPtr]::Zero) { return [Windows.Automation.AutomationElement]::FromHandle($handle) }
    }
    return $null
}

# Кнопка Attach в окне Потассиума ("Attach to running clients" и т.п.). Не нашлась — в $script:PotassiumButtons имена кнопок,
# какие есть (для журнала: вдруг Потассиум её переименовал).
function Find-PotassiumAttachButton {
    $script:PotassiumButtons = ''
    $window = Get-PotassiumWindowElement
    if (-not $window) { return $null }
    $buttons = @($window.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button))))
    $button = $buttons | Where-Object { $_.Current.Name -match '^\s*Attach\b' } | Select-Object -First 1
    if ($button) { return $button }
    $script:PotassiumButtons = (@($buttons | ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique) | Select-Object -First 20) -join ', '
    return $null
}

# Окно на сервере, а скрипт в нём так и не запустился за $ScriptStartTimeout сек после захода (автоаттач Потассиума
# не подключился или автоэкзекут не сработал) — окно надо перезапустить. Аккаунт, у которого скрипт не запустился
# 3 раза подряд, оставляем в покое на 30 минут (очередь запусков его не ждёт).
function Test-ScriptNotStarted($Account, $Tracker) {
    if ($ScriptStartTimeout -le 0 -or $NoPotassium -or $Tracker.ScriptSeen -or -not $Tracker.InGame -or -not $Tracker.JoinedAt) { return $false }
    if ($script:PotassiumSignedOutSince) { return $false }
    if ($Account.ScriptCheckPausedUntil -and (Get-Date) -lt $Account.ScriptCheckPausedUntil) { return $false }
    if (((Get-Date) - $Tracker.JoinedAt).TotalSeconds -lt $ScriptStartTimeout) { return $false }
    if ($Account.NoScript -ge 3) {
        $Account.ScriptCheckPausedUntil = (Get-Date).AddMinutes(30)
        $Account.NoScript = 0
        Write-Status "скрипт не запускается в этом окне 3 раза подряд — оставляю окно как есть на 30 минут (проверь $(if ($Executor -ne 'potassium') { "автоинжект и автоэкзекут в $($script:ExecName)" } else { 'автоаттач и автоэкзекут Потассиума' }))." Red $Account
        return $false
    }
    return $true
}

# После захода уменьшаем окно и сворачиваем: свёрнутый Роблокс почти не грузит видеокарту.
# Проверяем каждый цикл, а не один раз: Роблокс сам разворачивает окно — при прыжке на другой сервер, или главное
# игровое окно появляется позже того, что свернули, — и такие окна оставались растянутыми на весь экран.
# Первые 3 минуты после захода сворачиваем всегда; потом не трогаем окно, на которое ты сейчас смотришь.
# Окно, которым управляешь ты (UserOpen), не сворачиваем, пока оно живо: его развернули 3 раза за 10 сек (Роблокс сам
# разворачивает окно только при заходе на сервер, и не раз за разом) или оно было открыто, когда AutoRejoin его подхватил.
function Set-WindowCompact($Tracker, $Account) {
    if (-not $Tracker.EverJoined) { return }
    if ($Account.IsMain) {
        if (-not $NoMainFullscreen) { Set-WindowFullscreen $Tracker $Account }
        return   # мейн не уменьшаем и не сворачиваем
    }
    if ($NoMinimize) {
        # Окно хелпера, подхваченное после перезапуска AutoRejoin (например, после обновления на 3.8.3), могло остаться
        # свёрнутым с прежних версий — один раз возвращаем его на экран.
        if ($Tracker.Adopted -and -not $Tracker.PSObject.Properties['RestoreChecked']) {
            $Tracker | Add-Member -NotePropertyName RestoreChecked -NotePropertyValue $true -Force
            $null = [RejoinWindows]::RestoreMinimized([int]$Tracker.Pid)
        }
        return
    }
    if ($Tracker.UserOpen) { return }

    $open = @([RejoinWindows]::OpenWindows($Tracker.Pid))
    $wasOpen = $Tracker.SeenOpen
    $Tracker.SeenOpen = [bool]$open
    if ($open -and $Tracker.Adopted -and -not $Tracker.Compacted) {
        $Tracker.UserOpen = $true
        Write-Status 'окно было открыто до запуска AutoRejoin — не сворачиваю его, пока оно открыто.' Cyan $Account
        return
    }
    if ($open -and -not $wasOpen -and $Tracker.Compacted) {
        $now = Get-Date
        $Tracker.Restores = @(@($Tracker.Restores) + $now | Where-Object { ($now - $_).TotalSeconds -le 10 })
        if ($Tracker.Restores.Count -ge 3) {
            $Tracker.UserOpen = $true
            Write-Status 'окно разворачиваешь ты (3 раза за 10 сек) — больше его не сворачиваю, пока оно открыто.' Cyan $Account
            return
        }
    }
    # Сначала скрипт, потом сворачиваем: пусть автоаттач и запуск скрипта пройдут в обычном окне.
    if (Test-WaitScript $Tracker) { return }

    if (-not $open) {
        if (-not $Tracker.Compacted) {
            $Tracker.Compacted = $true
            Write-Status "окно уменьшено до ${WindowWidth}x${WindowHeight} и свёрнуто." DarkGray $Account
        }
        return
    }
    $settled = $Tracker.JoinedAt -and ((Get-Date) - $Tracker.JoinedAt).TotalSeconds -gt 180
    if ($Tracker.Compacted -and $settled -and $open -contains [RejoinWindows]::GetForegroundWindow()) { return }

    foreach ($handle in $open) { [RejoinWindows]::ShrinkAndMinimize($handle, $WindowWidth, $WindowHeight) }
    if (-not $Tracker.Compacted) {
        $Tracker.Compacted = $true
        Write-Status "окно уменьшено до ${WindowWidth}x${WindowHeight} и свёрнуто." DarkGray $Account
    } elseif (++$Tracker.Recompacted -le 3) {
        Write-Status 'Роблокс развернул окно сам — свернул обратно.' DarkGray $Account
    }
}

# Приоритет ЦП мейна. Роблокс сам опускает приоритет своих окон (свёрнутым — Idle, мейну — Normal каждые ~10 сек),
# поэтому окно отдаём RejoinPriority: он держит приоритет, пока окно живо.
function Set-MainPriority($Tracker, $Account) {
    if (-not $MainPriority -or $Tracker.PrioritySet -or -not $Account.IsMain) { return }
    if ([RejoinPriority]::Hold($Tracker.Pid, [uint32][Diagnostics.ProcessPriorityClass]$MainPriority)) {
        $Tracker.PrioritySet = $true
        Write-Status "приоритет ЦП: $MainPriority." DarkGray $Account
    }
}

# Обёртки над RejoinMemory (их подменяют проверки).
function Invoke-MemoryHold([int]$ProcessId) { return [RejoinMemory]::Hold($ProcessId, [long]$HelperMemCapMB * 1MB, [double]$MemFaultLimit) }
function Invoke-MemoryRelease([int]$ProcessId) { [RejoinMemory]::Release($ProcessId) }
function Get-MemoryEntry([int]$ProcessId) { return [RejoinMemory]::Find($ProcessId) }

# Потолок RAM хелпера (см. RejoinMemory): раз на окно, через минуту после захода и после запуска скрипта — пик загрузки
# уже позади. Мейна не трогаем; окно стало мейном или потолок выключили — снимаем.
function Set-HelperMemory($Tracker, $Account) {
    if ($Tracker.MemoryHeld) {
        if ($HelperMemCapMB -le 0 -or $Account.IsMain) {
            Invoke-MemoryRelease $Tracker.Pid
            $Tracker.MemoryHeld = $false
            Write-Status 'потолок RAM снят.' DarkGray $Account
        }
        return
    }
    if ($HelperMemCapMB -le 0 -or $Account.IsMain -or $Tracker.MemoryFailed -or -not $Tracker.EverJoined -or -not $Tracker.JoinedAt) { return }
    if (((Get-Date) - $Tracker.JoinedAt).TotalSeconds -lt 60 -or (Test-WaitScript $Tracker)) { return }
    $result = Invoke-MemoryHold $Tracker.Pid
    if ($result -ne 'ok') {
        $Tracker.MemoryFailed = $true
        Write-Status "потолок RAM не поставился: $result. Это окно остаётся без него." DarkYellow $Account
        return
    }
    $Tracker.MemoryHeld = $true
    $entry = Get-MemoryEntry $Tracker.Pid
    $before = if ($entry) { [int]($entry.WsBefore / 1MB) } else { 0 }
    $after = if ($entry) { [int]($entry.WsAfter / 1MB) } else { 0 }
    Write-Status ('потолок RAM {0} МБ: было в памяти {1} МБ, стало {2} МБ.' -f $HelperMemCapMB, $before, $after) DarkGray $Account
}

# Что RejoinMemory поменял сам (поднял / опустил / вернул потолок) — в журнал, с именем аккаунта.
function Write-MemoryEvents {
    foreach ($line in @([RejoinMemory]::DrainEvents())) {
        $parts = $line -split '\|', 2
        if ($parts.Count -lt 2) { continue }
        $account = @($script:Accounts | Where-Object { $_.Tracker -and $_.Tracker.Pid -eq [int]$parts[0] }) | Select-Object -First 1
        Write-Status $parts[1] DarkGray $account
    }
}

# Мейн не уменьшаем и не сворачиваем, а держим на весь экран. Полный экран Роблокс берёт при запуске из настройки
# Fullscreen (её пишет Set-LaunchFps). Если окно всё же открылось не на весь экран — после запуска скрипта жмём
# в нём F11 (до 2 раз). Когда окно на весь экран, больше его не трогаем: выйдешь из полного экрана сам — так и останется.
function Set-WindowFullscreen($Tracker, $Account) {
    # Окно, открытое до запуска AutoRejoin, оставляем каким его сделал ты.
    if ($Tracker.FullscreenTries -ge 2 -or $Tracker.Adopted) { return }
    if (Test-WaitScript $Tracker) { return }
    if ($Tracker.FullscreenAt -and ((Get-Date) - $Tracker.FullscreenAt).TotalSeconds -lt 5) { return }
    $open = @([RejoinWindows]::OpenWindows($Tracker.Pid))
    if (-not $open) { return }   # окно свёрнуто — значит, свернул ты сам
    if (@($open | Where-Object { [RejoinWindows]::IsFullscreen($_) }).Count) {
        $Tracker.FullscreenTries = 99
        Write-Status 'окно на весь экран.' DarkGray $Account
        return
    }
    $Tracker.FullscreenTries++
    $Tracker.FullscreenAt = Get-Date
    if ([RejoinWindows]::PressF11($open[0])) {
        Write-Status 'окно открылось не на весь экран — нажал в нём F11.' DarkGray $Account
    } elseif ($Tracker.FullscreenTries -ge 2) {
        Write-Status 'окно открылось не на весь экран, а вывести его вперёд Windows не дала — нажми в нём F11 сам.' DarkYellow $Account
    }
}

# Роблокс не принял вход в окне: билет не сработал (чаще всего из-за несовпадения номера браузера, см.
# Get-BrowserTrackerId), окно закрывается само через ~5 сек после этой строки в логе.
function Get-LoginFailText($Tracker) {
    $why = if ($Tracker.BtidMismatch) { ' — номер браузера в куке клиента не совпал' } else { '' }
    return "Роблокс не принял вход в окне (WebLogin $($Tracker.LoginError)$why)"
}

# Вход в окне не прошёл несколько раз подряд. Куки клиента мы сбрасываем перед каждым запуском, значит дело уже
# в самой сессии аккаунта: просим приложение взять новую куку (при входе по логину и паролю оно берёт её само,
# без человека), а если и это не помогло — пишем плашку.
function Register-LoginFailure($Account) {
    $Account.LoginFails++
    if ($Account.LoginFails -eq 2) {
        Write-Status 'Роблокс не принял вход в окне 2 раза подряд — прошу приложение взять новую куку.' DarkYellow $Account
        Request-AppAuth $Account 'expired'
    } elseif ($Account.LoginFails -eq 5) {
        Show-Alert "$($Account.Name): Роблокс не принимает вход в окне 5 раз подряд (403 на билет), новая кука не помогла. Войди в аккаунт в приложении заново."
    }
}

# Возвращает причину перезапуска окна или $null, если всё в порядке.
# Сколько ждать, что окно само вернётся на сервер после выхода в меню или дисконнекта без кода: в Bee Swarm —
# $OutOfGameGrace (прыжок на сервер ~0.2 сек), в других играх не меньше 45 сек (телепорт из лобби в матч).
function Get-OutOfGameGrace($Tracker) {
    if (-not $Tracker.PlaceId -or $Tracker.PlaceId -eq '1537690962') { return $OutOfGameGrace }
    return [Math]::Max($OutOfGameGrace, 45)
}

function Get-RestartReason($Tracker, $Account) {
    $now = Get-Date

    if ($Tracker.ForceReason) { return $Tracker.ForceReason }
    $memoryGB = try { $Tracker.Process.PrivateMemorySize64 / 1GB } catch { 0 }
    # Первые 2 минуты не трогаем: при загрузке окно ненадолго бывает больше обычного, а окно, которое
    # больше лимита уже на старте, иначе перезапускалось бы бесконечно.
    # Окном управляешь ты (см. Set-WindowCompact) — из-за памяти и скрипта его не перезапускаем, только если оно вылетело.
    # Мейна по лимиту можно не трогать (настройка "Не перезапускать мейна"); спасение при нехватке памяти (Test-LowMemory) — отдельно.
    $skipMain = $MemoryLimitSkipMain -and $Account -and $Account.IsMain
    if ($MemoryLimitGB -gt 0 -and -not $skipMain -and $memoryGB -gt $MemoryLimitGB -and ($now - $Tracker.Start).TotalSeconds -ge 120 -and -not $Tracker.UserOpen) {
        return ('окно раздулось до {0:N1} ГБ памяти (лимит {1} ГБ)' -f $memoryGB, $MemoryLimitGB)
    }

    # IsHungAppWindow отвечает сразу; Process.Responding ждал ответа от окна до 5 сек на каждом круге.
    $handle = [IntPtr]::Zero
    try { if ($Tracker.Process) { $handle = $Tracker.Process.MainWindowHandle } } catch {}
    $hung = $handle -ne [IntPtr]::Zero -and [RejoinWindows]::IsHungAppWindow($handle)
    if (-not $hung) { $Tracker.HangSince = $null }
    elseif (-not $Tracker.HangSince) { $Tracker.HangSince = $now }
    elseif (($now - $Tracker.HangSince).TotalSeconds -ge $HangTimeout) { return "окно не отвечает уже $HangTimeout сек" }

    # Без лога не понять, на сервере окно или нет, — тогда следим только за закрытием и зависанием.
    if (-not $Tracker.LogPath) { return $null }

    if (-not $Tracker.EverJoined) {
        # Вход не прошёл — ждать $JoinTimeout нечего, окно и само закроется через 5 сек.
        if ($Tracker.LoginError) { return (Get-LoginFailText $Tracker) }
        if (($now - $Tracker.Start).TotalSeconds -ge $JoinTimeout) { return "не зашёл на сервер за $JoinTimeout сек" }
        return $null
    }
    # Вылет по коду (кик, бездействие, потеря связи) — сразу, не дожидаясь $OutOfGameGrace. Секунду всё же даём:
    # вдруг следом в логе окажется заход на сервер.
    if ($Tracker.KickCode -and -not $Tracker.InGame -and ($now - $Tracker.KickAt).TotalSeconds -ge 1) {
        return (Get-KickText $Tracker.KickCode)
    }
    # Выход в меню Роблокса и непонятный дисконнект — ждём, не зайдёт ли окно само. Новый клиент проводит телепорт через
    # меню: "leaveUGCGameInternal", через 5–30 сек "Joining game" на новом месте (24.11 у игрока в Anime Adventures скрипт
    # фермы каждые ~6 мин телепортировал аккаунты из лобби в матч, а мы перезапускали окно через 1 сек после выхода в меню —
    # и аккаунты так и не доходили до матча). В Bee Swarm прыжок короткий, в остальных играх телепорт в матч дольше.
    $grace = Get-OutOfGameGrace $Tracker
    if (-not $Tracker.InGame -and $Tracker.LastReason -eq 'вышел в меню' -and ($now - $Tracker.StateSince).TotalSeconds -ge $grace) {
        return "вышел в меню Роблокса и за $grace сек не вернулся на сервер"
    }
    if (-not $Tracker.InGame -and ($now - $Tracker.StateSince).TotalSeconds -ge $grace) {
        $why = if ($Tracker.LastReason) { " ($($Tracker.LastReason))" } else { '' }
        return "отключило от сервера$why"
    }
    # Окно, открытое до запуска AutoRejoin, за отсутствие скрипта не перезапускаем: может, ты так его и запустил.
    if ($Account -and -not $Tracker.Adopted -and -not $Tracker.UserOpen -and (Test-ScriptNotStarted $Account $Tracker)) {
        $Account.NoScript++
        $why = if ($Executor -ne 'potassium') {
                if ($Tracker.Attached) { "$($script:ExecName) заинжектился, но скрипт фермы не запустился — проверь скрипт в autoexec $($script:ExecName)" }
                else { "$($script:ExecName) не заинжектился в окно — проверь автоинжект в $($script:ExecName)" }
            }
            elseif ($Tracker.Attached) { 'Потассиум подключился, но автоэкзекут не сработал' }
            elseif ($Tracker.ManualAttach -eq 'pressed') { 'автоаттач Потассиума не подключился, и наш Attach не помог' }
            else { 'автоаттач Потассиума к окну не подключился' }
        return "скрипт в окне не запустился за $ScriptStartTimeout сек ($why)"
    }
    if ($Account -and -not $Tracker.UserOpen -and (Test-ScriptMissing $Account)) {
        $Account.NoScript++
        return 'скрипт в окне перестал работать (нет пульса) — окно зависло в игре или скрипт упал'
    }
    return $null
}

#endregion

# Роблокс сам разрешает только одно окно: новое закрывает старое. Для этого он создаёт объект
# ROBLOX_singletonEvent. Если раньше него занять это имя мьютексом, Роблокс свой объект создать не может
# и окна друг друга не закрывают (так же делает Bloxstrap). Мьютексы живут, пока работает скрипт.
function Enable-MultiInstance {
    $script:SingletonLocks = @()
    $isNew = $false
    # Имя может держать пустой фоновый процесс Роблокса "в трее" — закрываем такие и пробуем ещё раз.
    for ($try = 1; $try -le 2; $try++) {
        try {
            $script:SingletonLocks += New-Object Threading.Mutex($true, 'ROBLOX_singletonEvent', [ref]$isNew)
            break
        } catch {
            if ($try -eq 1 -and (Stop-IdleTrays) -gt 0) { Start-Sleep -Seconds 2; continue }
            # Имя уже занято объектом другого типа — его создал Роблокс, открытый до скрипта.
            Write-Status 'Роблокс был открыт раньше скрипта, поэтому несколько окон не включилось. Закрой все окна Роблокса и перезапусти скрипт.' DarkYellow
            return
        }
    }
    # Старое имя из прошлых версий Роблокса — на всякий случай.
    try { $script:SingletonLocks += New-Object Threading.Mutex($true, 'ROBLOX_singletonMutex') } catch {}

    if ($isNew) { Write-Status 'Несколько окон Роблокса включено.' Green }
    else { Write-Status 'Несколько окон Роблокса уже включено другой программой.' Green }
}

# Froststrap запускает Роблокс строго по одному: каждый новый запуск ждёт, пока закончит предыдущий.
# Если запуск завис (например, на окне "Connectivity error" после обрыва связи), за ним в очередь встают
# все следующие, и окна не открываются вообще. Запуски старше $LaunchTimeout, не открывшие окно, закрываем.
function Clear-StuckLaunchers {
    $robloxParents = @(Get-CimInstance Win32_Process -Filter "Name='$PlayerProcess.exe'" | ForEach-Object { $_.ParentProcessId })
    $nowMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $stuck = @(Get-CimInstance Win32_Process -Filter "Name='Froststrap.exe' OR Name='Bloxstrap.exe' OR Name='Fishstrap.exe'" |
        Where-Object {
            $_.CommandLine -match '-player\s.*launchtime:(\d+)' -and
            ($nowMs - [long]$Matches[1]) / 1000 -ge $LaunchTimeout -and
            $robloxParents -notcontains $_.ProcessId
        })
    foreach ($process in $stuck) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
    if ($stuck) { Write-Status "закрыл зависшие запуски Froststrap: $($stuck.Count) шт." DarkYellow }
}

# При обрыве связи Froststrap показывает окно "Connectivity error" и ждёт, пока его закроют. Закрываем сразу,
# как кнопкой Close: Froststrap пропускает проверку обновлений Роблокса и продолжает запуск.
function Close-ConnectivityDialogs {
    $launchers = @(Get-Process -Name Froststrap, Bloxstrap, Fishstrap -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    if (-not $launchers) { return }
    if ([RejoinWindows]::CloseByTitle([int[]]$launchers, 'Connectivity error')) {
        Write-Status 'Froststrap не достучался до Роблокса (обрыв связи) — закрыл окно с ошибкой, запуск продолжается.' DarkYellow
    }
}

# Какая версия Роблокса сейчас актуальна (вида version-02c37bc51a384b8f). Спрашиваем раз в 10 минут.
function Get-LatestClientVersion {
    if ($script:LatestVersion -and ((Get-Date) - $script:LatestVersionAt).TotalMinutes -lt 10) { return $script:LatestVersion }
    try {
        $info = Invoke-RestMethod 'https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer' -TimeoutSec 10
        $script:LatestVersion = [string]$info.clientVersionUpload
        $script:LatestVersionAt = Get-Date
    } catch {}
    return $script:LatestVersion
}

# Froststrap перед каждым запуском 2–3 сек сверяет версию Роблокса по сети и раскладывает настройки, а потом
# запускает тот же RobloxPlayerBeta.exe с той же ссылкой roblox-player:. Запускаем его сами — сразу.
# Возвращает путь к RobloxPlayerBeta.exe или $null, если запускать надо через Froststrap: вышла новая версия
# (Froststrap её скачает), клиента нет на месте или задан -ViaBootstrapper.
function Get-DirectPlayerExe {
    if ($ViaBootstrapper) { return $null }
    $folder = $null
    foreach ($key in 'HKCU:\Software\Froststrap', 'HKCU:\Software\Bloxstrap', 'HKCU:\Software\Fishstrap') {
        $folder = try { (Get-ItemProperty -LiteralPath $key -ErrorAction Stop).PlayerPath } catch { $null }
        if ($folder) { break }
    }
    if (-not $folder) {
        $folder = Get-ChildItem "$env:LOCALAPPDATA\Froststrap\Versions", "$env:LOCALAPPDATA\Bloxstrap\Versions", "$env:LOCALAPPDATA\Roblox\Versions" -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName 'RobloxPlayerBeta.exe') } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $folder) { return $null }
    $exe = Join-Path $folder 'RobloxPlayerBeta.exe'
    if (-not (Test-Path -LiteralPath $exe)) { return $null }
    $latest = Get-LatestClientVersion
    if ($latest -and (Split-Path $folder -Leaf) -ne $latest) {
        if ($script:WarnedVersion -ne $latest) {
            $script:WarnedVersion = $latest
            Write-Status "Вышла новая версия Роблокса ($latest) — запускаю через Froststrap, он её скачает." DarkYellow
        }
        return $null
    }
    return $exe
}

# Лимит FPS — это настройка игры FramerateCap в GlobalBasicSettings. Роблокс читает её один раз при старте окна,
# файл общий для всех окон (флаг DFIntTaskSchedulerTargetFps Роблокс отклоняет: "Denied local configuration").
# Окна запускаются по одному, поэтому перед запуском аккаунта пишем его лимит, а после запуска ещё 5 сек удерживаем:
# работающие окна (особенно разведчики при прыжках) сами перезаписывают этот файл своим значением.
function Get-AccountFps($Account) {
    if ($Account.IsMain) { return $MainFps }
    return $HelperFps
}

# Полный экран (F11) — тоже настройка из этого файла (Fullscreen), её Роблокс тоже берёт при старте окна.
function Get-AccountFullscreen($Account) {
    return [bool](-not $NoMainFullscreen -and $Account.IsMain)
}

function Write-LaunchSettings([int]$Fps, [bool]$Fullscreen, [int]$Quality = -1) {
    $utf8 = New-Object Text.UTF8Encoding $false
    $fullscreenValue = if ($Fullscreen) { 'true' } else { 'false' }
    foreach ($file in @(Get-ChildItem -LiteralPath $RobloxDir -Filter 'GlobalBasicSettings_*.xml' -ErrorAction SilentlyContinue)) {
        try {
            $xml = [IO.File]::ReadAllText($file.FullName)
            $new = $xml
            if ($Fps -gt 0) {
                $new = if ($new -match '<int name="FramerateCap">-?\d+</int>') {
                    $new -replace '<int name="FramerateCap">-?\d+</int>', "<int name=`"FramerateCap`">$Fps</int>"
                } else {
                    $new -replace '(<Properties>)', "`$1`r`n`t`t<int name=`"FramerateCap`">$Fps</int>"
                }
            }
            if ($Quality -ge 0) {
                $new = if ($new -match '<token name="SavedQualityLevel">\d+</token>') {
                    $new -replace '<token name="SavedQualityLevel">\d+</token>', "<token name=`"SavedQualityLevel`">$Quality</token>"
                } else {
                    $new -replace '(<Properties>)', "`$1`r`n`t`t<token name=`"SavedQualityLevel`">$Quality</token>"
                }
            }
            $new = if ($new -match '<bool name="Fullscreen">\w+</bool>') {
                $new -replace '<bool name="Fullscreen">\w+</bool>', "<bool name=`"Fullscreen`">$fullscreenValue</bool>"
            } else {
                $new -replace '(<Properties>)', "`$1`r`n`t`t<bool name=`"Fullscreen`">$fullscreenValue</bool>"
            }
            if ($new -ne $xml) { [IO.File]::WriteAllText($file.FullName, $new, $utf8) }
        } catch {}   # файл как раз пишет окно Роблокса — следующая попытка через 0.25 сек
    }
}

function Set-LaunchFps($Account) {
    $Account.Fps = Get-AccountFps $Account
    $Account.Fullscreen = Get-AccountFullscreen $Account
    $Account.Quality = Set-LaunchQuality $Account
    Write-LaunchSettings $Account.Fps $Account.Fullscreen $Account.Quality
}

# Графика (SavedQualityLevel: 1–10, 0 — Automatic) из GlobalBasicSettings_*.xml — что там сейчас; $null — нет.
function Get-SavedQuality {
    $files = @(Get-ChildItem -LiteralPath $RobloxDir -Filter 'GlobalBasicSettings_*.xml' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    foreach ($file in $files) {
        try { if ([IO.File]::ReadAllText($file.FullName) -match '<token name="SavedQualityLevel">(\d+)</token>') { return [int]$Matches[1] } } catch {}
    }
    return $null
}

# quality.json: Main — графика мейна, Helper — что мы последний раз ставили хелперам. $null — файла нет.
function Get-QualityState {
    if (-not $script:QualityLoaded) {
        $script:QualityLoaded = $true
        $script:QualityState = try {
            $saved = Get-Content -LiteralPath $QualityFile -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json
            [pscustomobject]@{
                Main = if ($null -ne $saved.main) { [int]$saved.main } else { $null }
                Helper = if ($null -ne $saved.helper) { [int]$saved.helper } else { $null }
            }
        } catch { $null }
    }
    return $script:QualityState
}

function Set-QualityState($State) {
    $script:QualityState = $State
    $script:QualityLoaded = $true
    try {
        if ($State) {
            $json = [ordered]@{ main = $State.Main; helper = $State.Helper } | ConvertTo-Json -Compress
            [IO.File]::WriteAllText($QualityFile, $json, (New-Object Text.UTF8Encoding $false))
        } else {
            Remove-Item -LiteralPath $QualityFile -Force -ErrorAction SilentlyContinue
        }
    } catch {}
}

# Какую графику ставить окну при запуске: хелперу — $HelperQuality, мейну — его собственную. -1 — не трогать.
function Get-QualityTarget($Account, $State) {
    if (-not $Account.IsMain) { if ($HelperQuality -gt 0) { return $HelperQuality } else { return -1 } }
    if ($State -and $null -ne $State.Main) { return [int]$State.Main }
    return -1
}

# Графика у всех окон Роблокса общая (GlobalBasicSettings, окно читает её при старте) — как и лимит FPS, её пишем
# перед запуском окна. Своя графика мейна (её ставят в самой игре) запоминается в quality.json: значение в файле,
# которое не наше хелперское, — мейна; при запуске мейна оно возвращается. Графику хелперов выключили — мейну один
# раз возвращается его графика, quality.json удаляется, дальше графику не трогаем.
function Set-LaunchQuality($Account) {
    $state = Get-QualityState
    if ($HelperQuality -le 0 -and -not $state) { return -1 }
    if (-not $state) { $state = [pscustomobject]@{ Main = $null; Helper = $null } }
    $current = Get-SavedQuality
    if ($null -ne $current -and $current -ne $state.Helper) { $state.Main = $current }
    $target = Get-QualityTarget $Account $state
    if ($HelperQuality -gt 0 -and -not $Account.IsMain) { $state.Helper = $HelperQuality }
    if ($HelperQuality -le 0 -and $Account.IsMain) { Set-QualityState $null } else { Set-QualityState $state }
    return $target
}

# Настройки прошлого окна (FPS, полный экран, графика) ещё удерживаются, а у этого они другие — подождём, иначе перебьём их.
function Test-LaunchSettingsHeld($Account) {
    if (-not $script:FpsHoldUntil -or (Get-Date) -ge $script:FpsHoldUntil) { return $false }
    return ((Get-AccountFps $Account) -ne $script:FpsHoldValue -or (Get-AccountFullscreen $Account) -ne $script:FpsHoldFullscreen -or
        (Get-QualityTarget $Account (Get-QualityState)) -ne $script:FpsHoldQuality)
}

# Удержание настроек после запуска — без остановки скрипта: пока оно идёт, пауза главного цикла (Wait-Loop)
# каждые 0.25 сек переписывает FramerateCap, Fullscreen и графику, а проверка окон и Attach продолжают работать.
function Start-FpsHold($Account, [int]$Seconds) {
    $script:FpsHoldValue = $Account.Fps
    $script:FpsHoldFullscreen = $Account.Fullscreen
    $script:FpsHoldQuality = $Account.Quality
    $script:FpsHoldUntil = (Get-Date).AddSeconds($Seconds)
}

function Wait-Loop([double]$Seconds) {
    $until = (Get-Date).AddSeconds($Seconds)
    while ($true) {
        $now = Get-Date
        if ($script:FpsHoldUntil -and $now -lt $script:FpsHoldUntil) { Write-LaunchSettings $script:FpsHoldValue $script:FpsHoldFullscreen $script:FpsHoldQuality }
        $left = ($until - (Get-Date)).TotalMilliseconds
        if ($left -le 0) { break }
        # Пришла команда от приложения — просыпаемся сразу, её выполнит главный цикл.
        if ([RejoinInput]::Signal.WaitOne([int][Math]::Min(250, $left))) { break }
    }
}

# Окно умерло в первые секунды. 3 раза подряд — значит, новые окна не запустятся до перезагрузки Windows
# (так бывает после падения окна, к которому подключился Потассиум), и долбить запусками бесполезно.
function Register-InstantDeath {
    $script:InstantDeaths++
    if ($script:InstantDeaths -ge 3 -and -not $script:LaunchBlocked) {
        $script:LaunchBlocked = $true
        Write-Status 'Новые окна Роблокса умирают сразу после старта (3 раза подряд). Это лечится только перезагрузкой компьютера — новые запуски остановлены, открытые окна продолжаю держать.' Red
        if ($AutoReboot) { Invoke-AutoReboot }
    }
}

# 09.10 окна не запускались с 08:24 до 10:23 — пока не перезагрузили компьютер вручную. С -AutoReboot перезагружаемся
# сами (автозапуск всё поднимет за ~3 мин), но не чаще раза в 30 минут, чтобы не уйти в частые перезагрузки.
# Раньше было "не чаще раза в 2 часа" без повтора: 11.10 окна снова умерли через 2 мин после перезагрузки, и ферма
# простояла 14 часов. Теперь, если рано, перезагружаемся, как только пройдут 30 минут.
function Invoke-AutoReboot {
    $last = [datetime]::MinValue
    try { $last = [datetime]::Parse((Get-Content -LiteralPath $RebootStampFile -Raw -ErrorAction Stop).Trim()) } catch {}
    $next = $last.AddMinutes(30)
    if ((Get-Date) -lt $next) {
        $script:RebootRetryAt = $next
        Write-Status "Автоперезагрузка уже была в $($last.ToString('HH:mm')) — следующая не раньше $($next.ToString('HH:mm')), перезагружусь тогда (или перезагрузи сам)." Red
        return
    }
    try { Set-Content -LiteralPath $RebootStampFile -Value (Get-Date).ToString('o') -Encoding UTF8 } catch {}
    Request-Reboot 'Роблокс перестал запускать окна, это лечится перезагрузкой' Red
}

# Перезагрузка Windows сразу (user 21.10: "максимально ускорить перезагрузку"). Раньше было через 60 сек с кнопкой
# отмены, и Windows ещё ~6 сек закрывала окна Роблокса и Потассиум сама: 21.10 от команды до входа мейна прошло 2,5 мин.
# Теперь окна Роблокса и Потассиум закрываем сами (за доли секунды) и даём shutdown /r /f /t 0 — без отсчёта и без
# ожидания программ. Не вышло — окна поднимет обычная очередь запусков.
function Request-Reboot([string]$Why, [string]$Color = 'Yellow') {
    Write-Status "Перезагружаю компьютер сейчас: $Why. После перезагрузки AutoRejoin запустится сам и первым заведёт мейна." $Color
    Close-GameForReboot
    # Вывод shutdown.exe не перенаправляем (2>&1 при ErrorActionPreference=Stop в PowerShell 5.1 обрывает скрипт).
    & shutdown.exe /r /f /t 0 /d p:4:1 /c "AutoRejoin: $Why" | Out-Null
    # 1190 — перезагрузка уже назначена (например, Windows Update): тоже считаем назначенной.
    if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 1190) {
        $script:RebootAt = Get-Date
        Send-App ([ordered]@{ t = 'reboot'; at = $script:RebootAt.ToString('o'); why = $Why })
    } else {
        Write-Status "Windows не дала перезагрузить компьютер (shutdown.exe, код $LASTEXITCODE)." Red
    }
}

# Перед перезагрузкой: окна Роблокса и Потассиум закрываются сразу, чтобы Windows не ждала их.
function Close-GameForReboot {
    foreach ($name in @($PlayerProcess, 'Potassium')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Stop-Reboot {
    & shutdown.exe /a | Out-Null
    $script:RebootAt = $null
    $script:RebootRetryAt = $null
    # 1116 — отменять нечего (перезагрузку не назначали или уже отменили).
    if ($LASTEXITCODE -eq 0) { Write-Status 'Перезагрузка отменена.' Cyan }
    elseif ($LASTEXITCODE -eq 1116) { Write-Status 'Перезагрузка не назначена — отменять нечего.' DarkGray }
    else { Write-Status "не получилось отменить перезагрузку (shutdown.exe /a, код $LASTEXITCODE)." Red }
}

function Start-AccountLaunch($Account) {
    $script:LastLaunchAt = Get-Date
    try {
        if ($Account.Target.ShareCode) { Resolve-ShareLink $Account }
        Clear-ClientCookies
        $ticket = Get-AuthTicket $Account
        $uri = New-LaunchUri $ticket $Account.Target
        $exe = Get-DirectPlayerExe
        $Account.LaunchDirect = [bool]$exe
        Set-LaunchFps $Account
        $fpsNote = if ($Account.Fps) { " (лимит $($Account.Fps) FPS)" } else { '' }
        if ($exe) {
            Write-Status "запускаю...$fpsNote" Cyan $Account
            # Через WMI, а не Start-Process: так у окна Роблокса не будет "родителем" этот скрипт. Потассиум, когда чинит
            # клиент ("Fix it"), закрывает и программу, запустившую Роблокс, — 08.10 так закрылся сам AutoRejoin.
            $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
                CommandLine = "`"$exe`" $uri"; CurrentDirectory = (Split-Path $exe)
            }
            if ($created.ReturnValue -ne 0) { throw "Windows не запустила Роблокс (код $($created.ReturnValue))" }
        } else {
            try { Clear-StuckLaunchers } catch {}
            Write-Status "запускаю через Froststrap...$fpsNote" Cyan $Account
            Start-Process $uri
            # Через Froststrap Роблокс стартует на 2–3 сек позже — следующий запуск тоже отодвигаем, чтобы его лимит FPS
            # не перебил лимит этого окна.
            $script:LastLaunchAt = (Get-Date).AddSeconds(6)
        }
        $Account.LaunchedAt = Get-Date
        Start-FpsHold $Account $(if ($Account.LaunchDirect) { 5 } else { 12 })
        # Окно займёт память не сразу — учитываем его заранее, иначе параллельные запуски видели бы одну и ту же
        # "свободную" память.
        if ($null -ne $script:FreeMemoryGB) { $script:FreeMemoryGB -= $WindowMemoryGB }
    } catch {
        $message = $_.Exception.Message
        # Кука перестала работать прямо сейчас: повторять запуск с ней бесполезно (26.10 BarettAlt3 так 9,5 ч подряд
        # получал 403 на билет каждые 5 мин) — убираем аккаунт из очереди и просим приложение взять новую куку.
        if ($message -match '\(401\)' -or $message -match 'moderated') {
            Remove-ActiveAccount $Account
            $Account.LaunchedAt = $null
            $Account.FailTotal++
            $Account.LastError = if ($message -match '\(401\)') { 'кука перестала работать (401)' } else { 'Роблокс не пускает аккаунт (модерация)' }
            $Account.LastErrorAt = Get-Date
            Set-AuthProblem $Account $(if ($message -match '\(401\)') { 'expired' } else { 'moderated' })
            return
        }
        Register-JoinFail $Account "не получилось запустить: $message"
        $Account.NextLaunch = (Get-Date).AddSeconds((Get-Backoff $Account))
        Write-Status "не получилось запустить: $message. Повтор через $(Get-Backoff $Account) сек." Red $Account
    }
}

# Закрывает окно аккаунта (дальше его перезапустит обычная очередь запусков).
function Stop-AccountWindow($Account) {
    $tracker = $Account.Tracker
    if (-not $tracker) { return }
    Stop-Process -Id $tracker.Pid -Force -ErrorAction SilentlyContinue
    Add-UsedLog $tracker
    # Закрытый процесс исчезает не мгновенно: пока он виден, не принимаем его за открытое окно этого аккаунта
    # (раньше скрипт успевал "найти" закрываемое окно, следить за ним и терять на этом цикл).
    $script:KilledPids[$tracker.Pid] = $tracker.Start
    $script:Trackers.Remove($tracker.Pid)
    $tracker.Alive = $false
    if (-not $script:TrayDirtyAt) { $script:TrayDirtyAt = Get-Date }
    $Account.Tracker = $null
}

# Перезаход по команде (кнопка в приложении, расписание): закрываем окно, если оно есть, и запускаем заново через
# $Delay сек — без накопленных пауз за прошлые неудачи. Окно, которым управляешь ты (UserOpen), тоже: перезаход попросил ты.
function Restart-AccountWindow($Account, [string]$Reason, [double]$Delay = 0) {
    $Account.Fails = 0
    $Account.QuickExits = 0
    $Account.NoScript = 0
    $Account.ScriptCheckPausedUntil = $null
    if ($Account.LaunchedAt) { Write-Status "$Reason — окно и так сейчас запускается." DarkGray $Account; return }
    $Account.LastReason = $Reason
    $Account.LastReasonAt = Get-Date
    if ($Account.Tracker) {
        Write-Status "$Reason — перезахожу." Yellow $Account
        $Account.Restarts++
        Stop-AccountWindow $Account
    } else {
        Write-Status "$Reason — захожу." Cyan $Account
    }
    $Account.NextLaunch = (Get-Date).AddSeconds($Delay)
}

# Все аккаунты разом: закрываем все окна, через 10 сек они заходят заново по одному (обычная очередь). Разом, а не по
# очереди — чтобы приватный сервер опустел и следующим заходом поднялся новый; 10 сек — дать ему закрыться.
function Invoke-RejoinAll([string]$Reason, [string]$AccountReason) {
    if ($script:LaunchBlocked) {
        Write-Status "$($Reason): пропускаю — новые окна Роблокса сейчас не запускаются (лечится только перезагрузкой компьютера), открытые окна не трогаю." Red
        return
    }
    Reset-BalloonCycle   # все окна заходят заново — счёт продлений Balloon Blessing мейна начинается сначала
    $open = @($script:Accounts | Where-Object { $_.Tracker }).Count
    Write-Status "$($Reason): закрываю окна всех аккаунтов ($open) — через 10 сек зайдут заново по одному." Cyan
    foreach ($account in @($script:Accounts)) { Restart-AccountWindow $account $AccountReason 10 }
    foreach ($item in @($script:Deferred)) { $item.NextCheck = Get-Date }
}

function Step-Account($Account) {
    $now = Get-Date
    $tracker = $Account.Tracker

    # Окно оказалось другим аккаунтом (например, его запустили руками в ту же секунду) — отпускаем.
    if ($tracker -and $tracker.UserId -and $tracker.UserId -ne $Account.UserId) {
        $Account.Tracker = $tracker = $null
    }

    if ($tracker -and -not $tracker.Alive) {
        # Окно могло закрыться сразу после того, как Роблокс не принял вход, — дочитываем его лог (файл остался).
        if ($tracker.LogPath -and -not $tracker.EverJoined -and -not $tracker.LoginError) {
            try { Update-TrackerFromLog $tracker } catch {}
        }
        $closed = if ($tracker.LoginError) { Get-LoginFailText $tracker } else { 'окно закрылось' }
        Write-Status "$closed." Yellow $Account
        $Account.Restarts++
        $Account.LastReason = $closed
        $Account.LastReasonAt = $now
        if (-not $tracker.EverJoined) {
            Register-JoinFail $Account "$closed до захода на сервер"
            if (-not $tracker.LogPath) { $Account.Fails-- }   # окно без лога (убито сразу) пауза не растит — как раньше
        }
        if ($tracker.LoginError) { Register-LoginFailure $Account }
        # Окно умерло в первые секунды, не успев завести лог, — так античит Роблокса убивает окна после того,
        # как упало окно с Потассиумом. До перезагрузки Windows так будут падать все новые окна.
        if (-not $tracker.LogPath -and ($now - $tracker.Start).TotalSeconds -lt 20) { Register-InstantDeath }
        $Account.Tracker = $tracker = $null
        $Account.NextLaunch = $now.AddSeconds((Get-Backoff $Account))
    }

    if ($tracker) {
        if ($tracker.EverJoined -and -not $tracker.Announced) {
            $tracker.Announced = $true
            $Account.Fails = 0
            $Account.LoginFails = 0
            $script:InstantDeaths = 0
            $Account.JoinOk++
            Write-Status "в игре (place $($tracker.PlaceId))." Green $Account
        }
        Update-ScriptState $Account
        Update-BalloonRejoin $Account
        Set-MainPriority $tracker $Account
        Set-WindowCompact $tracker $Account
        Set-HelperMemory $tracker $Account
        $reason = Get-RestartReason $tracker $Account
        if ($reason) {
            Write-Status "$reason — перезахожу." Yellow $Account
            $Account.Restarts++
            $Account.LastReason = $reason
            $Account.LastReasonAt = $now
            Stop-AccountWindow $Account
            if (-not $tracker.EverJoined) {
                Register-JoinFail $Account $reason
                if ($tracker.LoginError) {
                    Register-LoginFailure $Account
                } elseif ($Account.Fails -ge 2) {
                    # Не зашёл на сервер дважды подряд — возможно, устарел сохранённый код VIP-сервера.
                    Set-ShareLinkStale $Account
                }
            }
            $delay = Get-Backoff $Account
            # Окно вылетает (ошибка, кик, дисконнект) меньше чем через минуту после захода уже не первый раз подряд —
            # что-то мешает (например, аккаунт зашёл с другого устройства): перезаходим с растущей паузой
            # 10, 20, 40 ... 300 сек, а не каждую секунду.
            if ($tracker.JoinedAt -and ($now - $tracker.JoinedAt).TotalSeconds -lt 60) { $Account.QuickExits++ } else { $Account.QuickExits = 0 }
            if ($Account.QuickExits -ge 2) {
                $delay = [Math]::Max($delay, [int][Math]::Min(10 * [Math]::Pow(2, $Account.QuickExits - 2), 300))
                Write-Status "вылетает сразу после захода $($Account.QuickExits) раз подряд — перезайду через $delay сек." DarkYellow $Account
            }
            $Account.NextLaunch = $now.AddSeconds($delay)
        }
        return
    }

    # Ждём окно, которое только что запустили для этого аккаунта.
    if ($Account.LaunchedAt) {
        $fresh = $script:Trackers.Values |
            Where-Object { $_.Start -ge $Account.LaunchedAt.AddSeconds(-2) -and -not (Get-Owner $_) -and (-not $_.UserId -or $_.UserId -eq $Account.UserId) } |
            Sort-Object Start |
            Select-Object -First 1
        if ($fresh) {
            $Account.Tracker = $fresh
            $Account.LaunchedAt = $null
        } elseif ($Account.LaunchDirect -and ($now - $Account.LaunchedAt).TotalSeconds -ge 20) {
            # Напрямую окно появляется за 1–2 сек. Нет его за 20 сек — оно умерло раньше, чем скрипт успел его
            # увидеть: так античит убивает новые окна после падения окна с Потассиумом.
            $Account.LaunchedAt = $null
            Register-JoinFail $Account 'окно умерло сразу после запуска'
            $Account.NextLaunch = $now.AddSeconds((Get-Backoff $Account))
            Write-Status 'окно умерло сразу после запуска.' Red $Account
            Register-InstantDeath
        } elseif (($now - $Account.LaunchedAt).TotalSeconds -ge $LaunchTimeout) {
            $Account.LaunchedAt = $null
            Register-JoinFail $Account "окно не открылось за $LaunchTimeout сек"
            $Account.NextLaunch = $now.AddSeconds((Get-Backoff $Account))
            Write-Status "окно не открылось за $LaunchTimeout сек." Red $Account
            try { Clear-StuckLaunchers } catch {}
        }
        return
    }

    # Окно этого аккаунта уже открыто (запущено до скрипта или руками) — просто следим за ним.
    $existing = $script:Trackers.Values |
        Where-Object { $_.UserId -eq $Account.UserId -and -not (Get-Owner $_) } |
        Sort-Object Start -Descending |
        Select-Object -First 1
    if ($existing) {
        $existing.Announced = $true
        $existing.Adopted = $true
        $Account.Tracker = $existing
        Write-Status "нашёл открытое окно (PID $($existing.Pid)), слежу." Cyan $Account
        return
    }

    if ($now -lt $Account.NextLaunch) { return }
    # Сначала мейн, потом остальные (Test-MainPending); мейну очередь освобождают (Clear-LoadingHelpers).
    if (-not $Account.IsMain -and ((Test-MainPending) -or (Test-MainDue))) { return }
    if ($Account.IsMain -and (Test-LaunchBusy)) { Clear-LoadingHelpers }
    if (($now - $script:LastLaunchAt).TotalSeconds -lt $LaunchGap) { return }
    if (Test-LaunchSettingsHeld $Account) { return }
    if ($script:LaunchBlocked) { return }
    if (Test-LaunchBusy) { return }
    if (-not (Test-LaunchMemory $Account)) { return }
    Start-AccountLaunch $Account
}

function Test-IsAdmin {
    $principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Ставит на системном диске файл подкачки фиксированного размера под число аккаунтов. Автоматический Windows
# держит маленьким (~3 ГБ при 16 ГБ RAM) и растит с опозданием: окна успевают упасть с "Out of Virtual Memory".
# Возвращает код выхода: 0 — готово, 1 — нет.
function Set-Pagefile {
    $count = $AccountCount
    if ($count -le 0) {
        Write-Status 'Нет аккаунтов — сначала добавь их: от их числа зависит размер подкачки.' Red
        return 1
    }

    $drive = $env:SystemDrive
    $name = "$drive\pagefile.sys"
    $ramGB = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB
    $needGB = Get-NeededCommitGB $count
    $currentGB = (@(Get-CimInstance Win32_PageFileUsage | Where-Object { $_.Name -eq $name }) | Measure-Object AllocatedBaseSize -Sum).Sum / 1KB
    $roomGB = (Get-PSDrive $drive.TrimEnd(':')).Free / 1GB + $currentGB - 10   # 10 ГБ на диске оставляем
    $sizeGB = [Math]::Min([Math]::Ceiling([Math]::Max(8, $needGB * 1.25 - $ramGB)), 64)
    if ($roomGB -lt $sizeGB) {
        if ($roomGB -lt 8) { Write-Status ('На диске {0} мало места: нужно {1} ГБ под подкачку.' -f $drive, $sizeGB) Red; return 1 }
        $sizeGB = [Math]::Floor($roomGB)
    }

    if (-not (Test-IsAdmin)) {
        Write-Status 'Нужны права администратора — подтверди запрос Windows.' Cyan
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -SetupPagefile -Elevated -DataDir `"$DataDir`" -AccountCount $count"
        try { $elevated = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -Wait -PassThru }
        catch { Write-Status 'Права администратора не даны — подкачка не изменена.' Red; return 1 }
        if ($elevated.ExitCode -ne 0) { Write-Status "Не получилось, подробности в $OwnLog." Red; return 1 }
        Write-Status ('Готово: файл подкачки {0} — {1} ГБ (было {2:N1} ГБ, {3} аккаунтов). ПЕРЕЗАГРУЗИ КОМПЬЮТЕР, чтобы заработало.' -f $name, $sizeGB, $currentGB, $count) Green
        return 0
    }

    try {
        $mb = [uint32]($sizeGB * 1024)
        $system = Get-CimInstance Win32_ComputerSystem
        if ($system.AutomaticManagedPagefile) { Set-CimInstance -InputObject $system -Property @{ AutomaticManagedPagefile = $false } }
        $setting = Get-CimInstance Win32_PageFileSetting | Where-Object { $_.Name -eq $name }
        if ($setting) { Set-CimInstance -InputObject $setting -Property @{ InitialSize = $mb; MaximumSize = $mb } }
        else { $null = Set-WmiInstance -Class Win32_PageFileSetting -Arguments @{ Name = $name; InitialSize = $mb; MaximumSize = $mb } }
    } catch {
        Write-Status "не получилось настроить подкачку: $($_.Exception.Message)" Red
        return 1
    }
    Write-Status ('Файл подкачки {0}: {1} ГБ, было {2:N1} ГБ. Нужна перезагрузка.' -f $name, $sizeGB, $currentGB) Green
    return 0
}

# После включения ПК (например, после отключения света) роутер поднимается позже компьютера. Ждём, пока Роблокс
# станет доступен, иначе все аккаунты отбраковались бы как "нерабочие" и скрипт бы закрылся.
# Возвращает $true, когда связь есть, $false — если приложение попросило остановиться.
function Wait-ForNetwork {
    $warned = $false
    # Проверяем каждую секунду (было раз в 10): после перезагрузки сеть поднимается за секунды, и мейн не должен ждать
    # лишнего.
    while (-not $script:StopRequested) {
        try {
            $request = [Net.HttpWebRequest]::Create('https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer')
            $request.Timeout = 3000
            $request.GetResponse().Dispose()
            if ($warned) { Write-Status 'Связь с Роблоксом появилась.' Green }
            return $true
        } catch {
            if (-not $warned) { $warned = $true; Write-Status 'Нет связи с Роблоксом (интернет ещё не поднялся?) — жду...' DarkYellow }
            if (Wait-Interruptible 1) { break }
        }
    }
    return $false
}

function Add-VerifiedAccount($Account, $Me) {
    if (@($script:Accounts | Where-Object { $_.UserId -eq [string]$Me.id })) {
        Write-Status "$($Me.name) уже есть в списке, пропускаю повтор." DarkYellow $Account
        $script:Problems[$Account.Key] = [pscustomobject]@{ Text = 'этот аккаунт уже есть в списке'; Cookie = $Account.Cookie; Link = $Account.OwnLink }
        return
    }
    $Account.UserId = [string]$Me.id
    $Account.Name = $Me.name
    $script:Problems.Remove($Account.Key)
    $script:AuthAsked.Remove($Account.Key)
    $script:Accounts += $Account
    Write-Status "подключен (id $($Account.UserId)), сервер: $(Get-LinkForLog $Account.Link)" Green $Account
}

# Попросить приложение взять аккаунту новую куку: при входе по логину и паролю оно делает это само (новая сессия из
# сохранённого входа или вход во встроенном браузере), при входе по куке — показывает плашку "обнови куку".
# С той же кукой — не чаще раза в 10 мин (приложение и само не дёргает человека чаще).
function Request-AppAuth($Account, [string]$Reason, [string]$Detail = '') {
    $last = $script:AuthAsked[$Account.Key]
    if ($last -and $last.Cookie -eq $Account.Cookie -and ((Get-Date) - $last.At).TotalMinutes -lt 10) { return }
    $script:AuthAsked[$Account.Key] = [pscustomobject]@{ Key = $Account.Key; At = Get-Date; Cookie = $Account.Cookie; IsMain = [bool]$Account.IsMain }
    Send-App ([ordered]@{ t = 'auth'; key = [string]$Account.Key; reason = $Reason; detail = $Detail })
}

# Что за модерация на аккаунте (страница roblox.com/not-approved) — для журнала. Спрашиваем раз на куку.
function Get-ModerationNote($Account) {
    if ($Account.ModerationNote -and $Account.ModerationNote.Cookie -eq $Account.Cookie) { return $Account.ModerationNote.Text }
    $text = ''
    try {
        $info = (Invoke-RobloxApi $Account GET 'https://usermoderation.roblox.com/v1/not-approved').Content | ConvertFrom-Json
        if ($info.punishmentTypeDescription) {
            $text = if ($info.punishmentTypeDescription -eq 'Warn') { 'предупреждение' } else { "бан ($($info.punishmentTypeDescription))" }
            if ($info.endDate) { try { $text += ' до ' + ([datetime]$info.endDate).ToLocalTime().ToString('dd.MM HH:mm') } catch {} }
            if ($info.messageToUser) {
                $message = [string]$info.messageToUser
                if ($message.Length -gt 200) { $message = $message.Substring(0, 200) + '…' }
                $text += ": $message"
            }
        }
    } catch {}
    $Account.ModerationNote = [pscustomobject]@{ Cookie = $Account.Cookie; Text = $text }
    return $text
}

# Кука аккаунта не работает. 401 — ждём новую куку (Problems), 403 "User is moderated" — откладываем и перепроверяем
# (Deferred). В обоих случаях просим приложение взять новую. Аккаунт из общей очереди убирает вызывающий.
function Set-AuthProblem($Account, [string]$Kind) {
    $byPassword = $Account.LoginMode -eq 'password'
    if ($Kind -eq 'expired') {
        $text = if ($byPassword) { 'кука устарела — AutoRejoin сам входит заново по логину и паролю' } else { 'кука устарела (401) — обнови её в приложении или включи вход по логину и паролю' }
        Write-Status "$text." $(if ($byPassword) { 'DarkYellow' } else { 'Red' }) $Account
        $script:Problems[$Account.Key] = [pscustomobject]@{ Text = $text; Cookie = $Account.Cookie; Link = $Account.OwnLink }
        Request-AppAuth $Account 'expired'
        return
    }
    $note = Get-ModerationNote $Account
    $what = if ($note) { $note } else { 'предупреждение или бан' }
    $minutes = if ($byPassword) { 10 } else { 30 }
    $todo = if ($byPassword) { 'открываю вход в аккаунт, чтобы его принять' } else { 'его нужно принять на roblox.com' }
    Write-Status "Роблокс не пускает аккаунт (403): $what — $todo. Остальные запускаю без него, проверю снова через $minutes мин." Red $Account
    $script:Deferred = @($script:Deferred | Where-Object { $_.Account.Key -ne $Account.Key })
    $script:Deferred += [pscustomobject]@{ Account = $Account; NextCheck = (Get-Date).AddMinutes($minutes); Note = "Роблокс не пускает аккаунт (403): $what" }
    Request-AppAuth $Account 'moderated' $note
}

# Убрать аккаунт из общей очереди (его кука перестала работать прямо на запуске окна).
function Remove-ActiveAccount($Account) {
    $script:Accounts = @($script:Accounts | Where-Object { $_ -ne $Account })
}

# Мейн при старте без рабочей куки, а вход у него по паролю: приложение обычно берёт новую за 1–3 сек (новая сессия
# из сохранённого входа) — ждём её до $Seconds сек, чтобы мейн всё равно зашёл первым. Возвращает запись vault.dat
# с новой кукой или $null. Отметку vault.dat не трогаем: остальное из файла применит обычный Sync-VaultAccounts.
function Wait-AppCookie($Account, [int]$Seconds) {
    if (-not $script:Out -or $Account.LoginMode -ne 'password') { return $null }
    $seen = Get-FileStamp $VaultFile
    $until = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $until) {
        if (Wait-Interruptible 0.5) { return $null }
        $stamp = Get-FileStamp $VaultFile
        if ($stamp -eq $seen) { continue }
        try { $vault = Read-Vault } catch { continue }   # файл как раз пишется
        $seen = $stamp
        $entry = Get-VaultEntries $vault | Where-Object { [string]$_.id -eq $Account.Key } | Select-Object -First 1
        if ($entry -and (Get-CleanCookie $entry.cookie) -ne $Account.Cookie) { return $entry }
    }
    return $null
}

# Проверяет куку и ставит аккаунт в работу. При старте (-Retry) — до 3 попыток с ожиданием интернета, на ходу — одна:
# не вышло — проверим снова через 2 мин; кука устарела (401) или Роблокс не пускает (403) — просим приложение взять
# новую куку (Set-AuthProblem).
function Add-AccountChecked($Account, [switch]$Retry) {
    $me = $null
    for ($try = 1; -not $me; $try++) {
        try {
            $me = (Invoke-RobloxApi $Account GET 'https://users.roblox.com/v1/users/authenticated').Content | ConvertFrom-Json
        } catch {
            $message = $_.Exception.Message
            # Протухшая кука — ждать бесполезно.
            if ($message -match '401') { Set-AuthProblem $Account 'expired'; return }
            # 403 на этот запрос — Роблокс ограничил аккаунт (предупреждение или бан). Остальные из-за него не ждут.
            if ($message -match '403') { Set-AuthProblem $Account 'moderated'; return }
            if (-not $Retry -or $try -ge 3) {
                Write-Status "не получилось проверить ($message) — запускаю остальные, этот проверю через 2 мин." DarkYellow $Account
                $script:Deferred += [pscustomobject]@{ Account = $Account; NextCheck = (Get-Date).AddMinutes(2); Note = 'не получилось проверить куку — повтор через 2 мин' }
                return
            }
            if (Wait-Interruptible 3) { return }
            if (-not (Wait-ForNetwork)) { return }
        }
    }
    Add-VerifiedAccount $Account $me
}

# Список аккаунтов поменяли в приложении: новые проверяем и ставим в работу, убранные и выключенные отпускаем
# (их окна остаются как есть), у остальных обновляем куку, мейн/хелпер и свою ссылку.
function Sync-VaultAccounts($Vault) {
    $entries = Get-VaultEntries $Vault
    $keys = @($entries | ForEach-Object { [string]$_.id })
    foreach ($account in @($script:Accounts)) {
        if ($keys -notcontains $account.Key) {
            $script:Accounts = @($script:Accounts | Where-Object { $_ -ne $account })
            Write-Status 'убран из списка или выключен — его окно больше не трогаю.' DarkGray $account
        }
    }
    $script:Deferred = @($script:Deferred | Where-Object { $keys -contains $_.Account.Key })
    foreach ($key in @($script:Problems.Keys)) { if ($keys -notcontains $key) { $script:Problems.Remove($key) } }

    foreach ($entry in $entries) {
        $key = [string]$entry.id
        $cookie = Get-CleanCookie $entry.cookie
        $account = @(@($script:Accounts) + @($script:Deferred | ForEach-Object { $_.Account })) | Where-Object { $_.Key -eq $key } | Select-Object -First 1
        if ($account) {
            # Кука другого человека Роблокса под тем же ключом (аккаунт в приложении заменили) — как новый аккаунт.
            if ($entry.userId -and $account.UserId -and [string]$entry.userId -ne $account.UserId) {
                $script:Accounts = @($script:Accounts | Where-Object { $_ -ne $account })
                $script:Deferred = @($script:Deferred | Where-Object { $_.Account -ne $account })
                $account = $null
            } else {
                if ($account.Cookie -ne $cookie) {
                    $account.Cookie = $cookie
                    $account.Csrf = $null
                    $script:AuthAsked.Remove($key)
                    # Отложенный (403) аккаунт с новой кукой — проверить сразу, а не через 10–30 мин.
                    $item = $script:Deferred | Where-Object { $_.Account -eq $account } | Select-Object -First 1
                    if ($item) { $item.NextCheck = Get-Date }
                    Write-Status 'кука обновлена.' DarkGray $account
                }
                Update-AccountEntry $account $entry
                continue
            }
        }
        $ownLink = Get-EntryOwnLink $entry
        $problem = $script:Problems[$key]
        if ($problem -and $problem.Cookie -eq $cookie -and $problem.Link -eq $ownLink) { continue }   # всё то же — ждём, пока поправят
        $script:Problems.Remove($key)
        try { $account = New-VaultAccount $entry }
        catch {
            $script:Problems[$key] = [pscustomobject]@{ Text = "своя ссылка не подходит: $($_.Exception.Message)"; Cookie = $cookie; Link = $ownLink }
            Write-Status "аккаунт $($entry.name): своя ссылка не подходит — $($_.Exception.Message)" Red
            continue
        }
        Write-Status 'добавлен в приложении — проверяю куку.' Cyan $account
        Add-AccountChecked $account
    }
}

# Отложенные при старте аккаунты (403 — Роблокс не пускает, или не удалось проверить): когда подходит срок,
# проверяем снова; пустили — аккаунт сразу встаёт в общую очередь запуска.
function Update-DeferredAccounts {
    foreach ($item in @($script:Deferred)) {
        if ((Get-Date) -lt $item.NextCheck) { continue }
        try {
            $me = (Invoke-RobloxApi $item.Account GET 'https://users.roblox.com/v1/users/authenticated').Content | ConvertFrom-Json
        } catch {
            $message = $_.Exception.Message
            if ($message -match '401') {
                # Кука за это время устарела — ждём новую из приложения, а не стучимся каждые 30 мин.
                $script:Deferred = @($script:Deferred | Where-Object { $_ -ne $item })
                Set-AuthProblem $item.Account 'expired'
                continue
            }
            $byPassword = $item.Account.LoginMode -eq 'password'
            $item.NextCheck = (Get-Date).AddMinutes($(if ($byPassword) { 10 } else { 30 }))
            if ($message -match '403') { Request-AppAuth $item.Account 'moderated' (Get-ModerationNote $item.Account) }
            continue
        }
        $script:Deferred = @($script:Deferred | Where-Object { $_ -ne $item })
        Write-Status 'Роблокс снова пускает аккаунт — добавляю в работу.' Green $item.Account
        Add-VerifiedAccount $item.Account $me
    }
}

# Раньше AutoRejoin клал в автоэкзекут Потассиума antiafk.txt (защита от кика за бездействие). Он больше не нужен:
# убираем его, если это наш файл (в первой строке пометка "AutoRejoin").
function Remove-AntiAfk {
    $path = Join-Path $env:LOCALAPPDATA 'Potassium\autoexec\antiafk.txt'
    try {
        if ((Test-Path -LiteralPath $path) -and ([IO.File]::ReadAllText($path) -match '^-- AutoRejoin:')) {
            Remove-Item -LiteralPath $path -Force
            Write-Status 'Убрал antiafk.txt из автоэкзекута Потассиума.' DarkGray
        }
    } catch { Write-Status "не получилось убрать antiafk.txt: $($_.Exception.Message)" DarkYellow }
}

#region Приложение: настройки на ходу, команды, расписание, состояние окон

# Код выхода для приложения: 3 — делать нечего (нет включённых аккаунтов, не читается vault), перезапускать бесполезно;
# 4 — движок уже запущен (другая копия или старый AutoRejoin в окне консоли).
$script:ExitCode = 0

function Get-UtcNow { return [DateTime]::UtcNow }

function Format-Utc($Value) {
    if ($null -eq $Value) { return $null }
    return ([datetime]$Value).ToUniversalTime().ToString('o')
}

function Get-FileStamp([string]$Path) {
    try { $item = Get-Item -LiteralPath $Path -ErrorAction Stop; return '{0}|{1}' -f $item.LastWriteTimeUtc.Ticks, $item.Length } catch { return '' }
}

# Значение из config.json: свойство $Name (вложенное — через точку: 'schedule.time'); нет его или оно null — $Default.
function Get-ConfigValue($Config, [string]$Name, $Default) {
    $value = $Config
    foreach ($part in $Name -split '\.') {
        if ($null -eq $value) { return $Default }
        $property = $value.PSObject.Properties[$part]
        if (-not $property) { return $Default }
        $value = $property.Value
    }
    if ($null -eq $value) { return $Default }
    return $value
}

function Get-ConfigNumber($Config, [string]$Name, [double]$Default, [double]$Min, [double]$Max) {
    $text = ([string](Get-ConfigValue $Config $Name $Default)).Replace(',', '.')
    $number = 0.0
    if (-not [double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) { $number = $Default }
    return [Math]::Min([Math]::Max($number, $Min), $Max)
}

function Get-ConfigFlag($Config, [string]$Name, [bool]$Default) {
    $value = Get-ConfigValue $Config $Name $Default
    if ($value -is [bool]) { return $value }
    return [string]$value -in 'true', '1', 'yes', 'on'
}

# Применяет config.json. Возвращает $true, если поменялось расписание.
function Set-EngineConfig($Config) {
    $script:Link = [string](Get-ConfigValue $Config 'link' '')
    $script:LinkFile = [string](Get-ConfigValue $Config 'linkFile' '')
    # Игра по умолчанию (выбирается в приложении: Настройки -> Сервер -> Игра).
    $game = [string](Get-ConfigValue $Config 'gamePlaceId' '')
    $script:DefaultPlace = if ($game -match '^\d{4,20}$') { $game } else { '1537690962' }
    $script:DefaultGameName = if ($script:DefaultPlace -eq '1537690962') { 'Bee Swarm Simulator' } else { $name = [string](Get-ConfigValue $Config 'gameName' ''); if ($name) { $name } else { "игра $($script:DefaultPlace)" } }
    $wasMinimizing = -not $script:NoMinimize
    $script:NoMinimize = -not (Get-ConfigFlag $Config 'minimizeWindows' $false)
    # Сворачивание выключили (или обновились на 3.8.3) — уже свёрнутые окна хелперов возвращаем на экран.
    if ($wasMinimizing -and $script:NoMinimize -and $script:Trackers) {
        $restored = 0
        foreach ($tracker in @($script:Trackers.Values)) {
            if (-not $tracker.Alive) { continue }
            $restored += [RejoinWindows]::RestoreMinimized([int]$tracker.Pid)
        }
        if ($restored) { Write-Status "Окна хелперов больше не сворачиваю — вернул на экран: $restored." Cyan }
    }
    $script:WindowWidth = [int](Get-ConfigNumber $Config 'windowWidth' 800 320 3840)
    $script:WindowHeight = [int](Get-ConfigNumber $Config 'windowHeight' 600 240 2160)
    $script:NoMainFullscreen = -not (Get-ConfigFlag $Config 'mainFullscreen' $false)
    $script:MainFps = [int](Get-ConfigNumber $Config 'mainFps' 60 0 1000)
    $script:HelperFps = [int](Get-ConfigNumber $Config 'helperFps' 30 0 1000)
    $priority = [string](Get-ConfigValue $Config 'mainPriority' '')
    $script:MainPriority = if ($priority -in 'Idle', 'BelowNormal', 'Normal', 'AboveNormal', 'High') { $priority } else { '' }
    $script:MemoryLimitGB = if (Get-ConfigFlag $Config 'memoryLimitOn' $true) { Get-ConfigNumber $Config 'memoryLimitGB' 8 1 256 } else { 0 }
    $script:MemoryLimitSkipMain = Get-ConfigFlag $Config 'memoryLimitSkipMain' $false
    $script:WindowMemoryGB = if (Get-ConfigFlag $Config 'windowMemoryOn' $true) { Get-ConfigNumber $Config 'windowMemoryGB' 3.5 0.5 64 } else { 0 }
    $script:MinFreeMemoryGB = if (Get-ConfigFlag $Config 'minFreeMemoryOn' $true) { Get-ConfigNumber $Config 'minFreeMemoryGB' 1.5 0.2 64 } else { 0 }
    $script:ViaBootstrapper = Get-ConfigFlag $Config 'useBootstrapper' $false
    $script:AutoReboot = Get-ConfigFlag $Config 'autoReboot' $false
    $script:NoPotassium = -not (Get-ConfigFlag $Config 'potassium' $true)
    $exec = [string](Get-ConfigValue $Config 'executor' 'potassium')
    $script:Executor = if ($exec -in 'real', 'volt', 'synapsez') { $exec } else { 'potassium' }
    $path = ([string](Get-ConfigValue $Config 'executorPath' '')).Trim().Trim('"')
    if ($path -ne $script:ExecutorPath -or $script:ExecConfigured -ne $script:Executor) {
        $script:ExecutorPath = $path
        $script:ExecConfigured = $script:Executor
        Set-ExecProfile
    }
    $script:Workspace = if ($script:Executor -eq 'potassium') { Join-Path $env:LOCALAPPDATA 'Potassium\workspace' } else { Join-Path $script:ExecDir 'workspace' }
    $script:PotassiumPath = [string](Get-ConfigValue $Config 'potassiumPath' '')
    $script:KeepAutoAttach = Get-ConfigFlag $Config 'keepAutoAttach' $true
    $script:PotassiumAutoLogin = Get-ConfigFlag $Config 'potassiumAutoLogin' $false
    $script:ManualAttachDelay = [int](Get-ConfigNumber $Config 'advanced.manualAttachDelay' 25 0 120)
    $script:ScriptStartTimeout = if (Get-ConfigFlag $Config 'scriptCheck' $false) { [int](Get-ConfigNumber $Config 'scriptStartTimeout' 60 15 600) } else { 0 }
    $script:ScriptTimeout = if (Get-ConfigFlag $Config 'pulseCheck' $false) { [int](Get-ConfigNumber $Config 'scriptTimeout' 90 30 1800) } else { 0 }
    $script:BalloonRejoinCount = if (Get-ConfigFlag $Config 'balloonRejoin' $false) { [int](Get-ConfigNumber $Config 'balloonRejoinCount' 6 1 50) } else { 0 }
    $script:BalloonQuietMinutes = [int](Get-ConfigNumber $Config 'balloonQuietMinutes' 120 0 720)
    $oldCap = $script:HelperMemCapMB
    $oldFaultLimit = $script:MemFaultLimit
    $script:HelperMemCapMB = if (Get-ConfigFlag $Config 'helperMemCapOn' $false) { [int](Get-ConfigNumber $Config 'helperMemCapMB' 700 300 4096) } else { 0 }
    $script:MemFaultLimit = [int](Get-ConfigNumber $Config 'advanced.memFaultLimit' 3000 500 50000)
    $script:HelperQuality = if (Get-ConfigFlag $Config 'helperQualityOn' $false) { [int](Get-ConfigNumber $Config 'helperQuality' 1 1 10) } else { 0 }
    # Потолок поменяли на ходу — окнам, у которых он уже стоит, сразу новый (выключили — снимет Set-HelperMemory).
    if ($script:HelperMemCapMB -gt 0 -and ($script:HelperMemCapMB -ne $oldCap -or $script:MemFaultLimit -ne $oldFaultLimit)) {
        [RejoinMemory]::SetLimits([long]$script:HelperMemCapMB * 1MB, [double]$script:MemFaultLimit)
    }
    $script:CheckInterval = Get-ConfigNumber $Config 'advanced.checkInterval' 1 0.5 10
    $script:LaunchTimeout = [int](Get-ConfigNumber $Config 'advanced.launchTimeout' 120 30 600)
    $script:JoinTimeout = [int](Get-ConfigNumber $Config 'advanced.joinTimeout' 60 20 600)
    $script:OutOfGameGrace = [int](Get-ConfigNumber $Config 'advanced.outOfGameGrace' 15 3 300)
    $script:HangTimeout = [int](Get-ConfigNumber $Config 'advanced.hangTimeout' 60 10 600)
    $script:RelaunchDelay = [int](Get-ConfigNumber $Config 'advanced.relaunchDelay' 1 0 60)
    $script:LaunchGap = [int](Get-ConfigNumber $Config 'advanced.launchGap' 2 0 60)
    $script:LaunchCooldown = [int](Get-ConfigNumber $Config 'advanced.launchCooldown' 3 0 600)
    $script:PotassiumWarmup = [int](Get-ConfigNumber $Config 'advanced.potassiumWarmup' 3 1 30)

    $time = [string](Get-ConfigValue $Config 'schedule.time' '09:51')
    $time = if ($time -match '^\s*([01]?\d|2[0-3]):([0-5]\d)\s*$') { '{0:D2}:{1}' -f [int]$Matches[1], $Matches[2] } else { '09:51' }
    $schedule = [pscustomobject]@{
        Enabled = Get-ConfigFlag $Config 'schedule.enabled' $false
        Time    = $time
        Zone    = [string](Get-ConfigValue $Config 'schedule.timeZone' '')
        Action  = if ([string](Get-ConfigValue $Config 'schedule.action' 'rejoin') -eq 'reboot') { 'reboot' } else { 'rejoin' }
    }
    $changed = ($schedule | ConvertTo-Json -Compress) -ne ($script:Schedule | ConvertTo-Json -Compress)
    $script:Schedule = $schedule
    return $changed
}

# Раз в 2 сек: поменялись config.json или vault.dat — применяем на ходу, без перезапуска.
function Update-AppConfig([switch]$Force) {
    $now = Get-Date
    if (-not $Force -and ($now - $script:ConfigCheckAt).TotalSeconds -lt 2) { return }
    $script:ConfigCheckAt = $now

    $stamp = Get-FileStamp $ConfigFile
    if ($Force -or $stamp -ne $script:ConfigStamp) {
        $config = $null
        try {
            $config = if ($stamp) { Get-Content -LiteralPath $ConfigFile -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
        } catch { Write-Status "config.json не читается: $($_.Exception.Message)" Red; $script:ConfigStamp = $stamp }
        if ($config) {
            $first = $null -eq $script:ConfigStamp
            $oldPriority = $MainPriority
            $oldLink = "$Link|$LinkFile"
            $scheduleChanged = Set-EngineConfig $config
            $script:ConfigStamp = $stamp
            if ("$Link|$LinkFile" -ne $oldLink) { $script:DefaultLinkCheckedAt = [datetime]::MinValue }
            if ($MainPriority -ne $oldPriority) {
                foreach ($account in @($script:Accounts | Where-Object { $_.IsMain -and $_.Tracker })) {
                    if (-not $MainPriority) { [RejoinPriority]::Release($account.Tracker.Pid) }
                    $account.Tracker.PrioritySet = $false
                }
            }
            if ($scheduleChanged) {
                # Новое расписание действует с этой минуты: время, которое уже прошло сегодня, — только завтра.
                $script:ScheduleArmedAt = Get-UtcNow
                if (-not $first) { Write-Status (Get-ScheduleText) Cyan }
            }
            if (-not $first) { Write-Status 'Настройки обновлены.' DarkGray }
        }
    }

    if ($script:Phase -ne 'running') { return }   # при старте аккаунты читает Start-Watchdog
    $stamp = Get-FileStamp $VaultFile
    if ($stamp -ne $script:VaultStamp) {
        try { $vault = Read-Vault } catch { return }   # файл как раз пишется — прочитаем в следующий раз
        $script:VaultStamp = $stamp
        $script:VaultPotassium = $vault.potassium
        Sync-VaultAccounts $vault
    }
}

function Get-ScheduleZone {
    if ($script:Schedule.Zone) { try { return [TimeZoneInfo]::FindSystemTimeZoneById($script:Schedule.Zone) } catch {} }
    return [TimeZoneInfo]::Local
}

function Get-ScheduleText {
    if (-not $script:Schedule.Enabled) { return 'Плановый перезапуск выключен.' }
    $what = if ($script:Schedule.Action -eq 'reboot') { 'перезагрузка компьютера' } else { 'перезаход всеми аккаунтами' }
    return "Плановый перезапуск: каждый день в $($script:Schedule.Time) ($((Get-ScheduleZone).DisplayName)) — $what."
}

# Ближайший момент по расписанию (UTC) не раньше $FromUtc: время $Time ('HH:mm') в часовом поясе $Zone.
function Get-ScheduleTargetUtc([datetime]$FromUtc, [string]$Time, [TimeZoneInfo]$Zone) {
    $from = [datetime]::SpecifyKind($FromUtc, [DateTimeKind]::Utc)
    $hour, $minute = [int[]]($Time -split ':')
    $day = [TimeZoneInfo]::ConvertTimeFromUtc($from, $Zone).Date
    for ($i = -1; $i -le 2; $i++) {
        $wall = [datetime]::SpecifyKind($day.AddDays($i).AddHours($hour).AddMinutes($minute), [DateTimeKind]::Unspecified)
        # Переход на летнее время: такого времени в этот день нет — берём на час позже.
        if ($Zone.IsInvalidTime($wall)) { $wall = $wall.AddHours(1) }
        $utc = [TimeZoneInfo]::ConvertTimeToUtc($wall, $Zone)
        if ($utc -ge $from) { return $utc }
    }
    return $null
}

function Read-ScheduleStamp {
    try {
        $text = (Get-Content -LiteralPath $ScheduleStampFile -Raw -ErrorAction Stop).Trim()
        return [datetime]::Parse($text, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    } catch { return $null }
}

# Плановый перезапуск: каждый день в заданное время. Срабатывает, только если в этот момент движок уже работал с этим
# расписанием (ScheduleArmedAt): после перезагрузки по расписанию или ПК, включённого позже, второй раз не сработает.
# Цикл мог задержаться (запуск окна и т.п.) — на это даём 15 минут.
function Update-Schedule {
    if (-not $script:Schedule.Enabled) { $script:ScheduleNextUtc = $null; return }
    $zone = Get-ScheduleZone
    $nowUtc = Get-UtcNow
    $due = Get-ScheduleTargetUtc $nowUtc.AddMinutes(-15) $script:Schedule.Time $zone
    if ($due -and $due -le $nowUtc -and $due -gt $script:ScheduleArmedAt -and (-not $script:ScheduleLast -or $due -gt $script:ScheduleLast)) {
        $script:ScheduleLast = $due
        # Отметку пишем до перезагрузки: после неё движок не должен сработать на тот же момент ещё раз.
        try { Set-Content -LiteralPath $ScheduleStampFile -Value $due.ToString('o') -Encoding UTF8 } catch {}
        Invoke-ScheduledAction "каждый день в $($script:Schedule.Time), $($zone.DisplayName)"
    }
    $script:ScheduleNextUtc = Get-ScheduleTargetUtc $nowUtc.AddSeconds(1) $script:Schedule.Time $zone
}

function Invoke-ScheduledAction([string]$When) {
    if ($script:Schedule.Action -eq 'reboot') { Request-Reboot "плановая перезагрузка ($When)" }
    else { Invoke-RejoinAll "Плановый перезаход ($When)" 'плановый перезаход' }
}

# Пауза, которую прерывает команда от приложения. Возвращает $true, если попросили остановиться.
function Wait-Interruptible([double]$Seconds) {
    $until = (Get-Date).AddSeconds($Seconds)
    while (-not $script:StopRequested) {
        Invoke-AppCommands
        if ($script:StopRequested) { break }
        Write-AppStatus
        $left = ($until - (Get-Date)).TotalMilliseconds
        if ($left -le 0) { break }
        $null = [RejoinInput]::Signal.WaitOne([int][Math]::Min(1000, $left))
    }
    return $script:StopRequested
}

# Команды от приложения (по строке из stdin).
function Invoke-AppCommands {
    if ([RejoinInput]::Closed -and -not $script:GuiGoneLogged) {
        $script:GuiGoneLogged = $true
        $script:Out = $null
        Write-Status 'Приложение закрылось — продолжаю держать аккаунты в игре без него.' DarkYellow
    }
    while ($true) {
        $line = [RejoinInput]::Next()
        if ($null -eq $line) { return }
        $command, $argument = $line -split '\s+', 2
        $running = $script:Phase -eq 'running'
        switch ($command) {
            'stop'   { $script:StopRequested = $true }
            'reload' { $script:ConfigCheckAt = [datetime]::MinValue }
            'status' { $script:StatusAt = [datetime]::MinValue }
            'rejoin-all' {
                if ($running) { Invoke-RejoinAll 'Перезаход всеми аккаунтами (кнопка в приложении)' 'перезаход по кнопке' }
                else { Write-Status 'Перезайти всеми можно, когда аккаунты уже запущены.' DarkGray }
            }
            'rejoin' {
                $account = $script:Accounts | Where-Object { $_.Key -eq $argument } | Select-Object -First 1
                $item = $script:Deferred | Where-Object { $_.Account.Key -eq $argument } | Select-Object -First 1
                if ($account -and $running) { Restart-AccountWindow $account 'перезаход по кнопке' }
                elseif ($item) { $item.NextCheck = Get-Date; Write-Status 'проверяю аккаунт сейчас.' Cyan $item.Account }
            }
            'cancel-reboot' { Stop-Reboot }
            'schedule-now' {
                if ($running) { Invoke-ScheduledAction 'по кнопке «Выполнить сейчас»' }
                else { Write-Status 'Выполнить плановый перезапуск можно, когда аккаунты уже запущены.' DarkGray }
            }
            default { Write-Status "неизвестная команда от приложения: $command" DarkGray }
        }
    }
}

# Что показать в приложении про аккаунт: состояние (state) и пояснение (text).
function Get-AccountView($Account, [string]$State, [string]$Text) {
    $now = Get-Date
    $tracker = $Account.Tracker
    $memoryGB = $null; $wsGB = $null; $capMB = $null; $windowPid = $null; $started = $null; $scriptState = 'off'
    if ($tracker) {
        $windowPid = $tracker.Pid
        $memoryGB = try { [Math]::Round($tracker.Process.PrivateMemorySize64 / 1GB, 2) } catch { $null }   # commit: сколько окно заняло
        $wsGB = try { [Math]::Round($tracker.Process.WorkingSet64 / 1GB, 2) } catch { $null }             # сколько из этого сейчас в RAM
        if ($tracker.MemoryHeld) {
            $entry = try { Get-MemoryEntry $tracker.Pid } catch { $null }
            if ($entry) { $capMB = [int]($entry.Cap / 1MB) }
        }
        $started = $tracker.Start
        if (-not $NoPotassium -and $ScriptStartTimeout -gt 0) { $scriptState = if ($tracker.ScriptSeen) { 'ok' } else { 'wait' } }
        if (-not $State) {
            if (-not $tracker.EverJoined) { $State = 'loading'; $Text = 'заходит на сервер' }
            elseif (-not $tracker.InGame) {
                $State = 'offline'
                $Text = if ($tracker.LastReason) { "отключился от сервера ($($tracker.LastReason))" } else { 'отключился от сервера' }
            }
            elseif (Test-WaitScript $tracker) { $State = 'script'; $Text = 'в игре, ждёт запуска скрипта' }
            else { $State = 'ingame'; $Text = if ($scriptState -eq 'ok') { 'в игре, скрипт работает' } else { 'в игре' } }
        }
    } elseif (-not $State) {
        if ($Account.LaunchedAt) { $State = 'launching'; $Text = 'запускается' }
        elseif ($script:LaunchBlocked) { $State = 'blocked'; $Text = 'новые окна Роблокса не запускаются — нужна перезагрузка компьютера' }
        else {
            # NextLaunch у ещё не запускавшегося аккаунта — [datetime]::MinValue: секунд до него не влезает в int.
            $due = ($Account.NextLaunch - $now).TotalSeconds
            if ($due -gt 1) { $State = 'waiting'; $Text = "перезайдёт через $([Math]::Ceiling($due)) сек" }
            elseif ($Account.MemoryWait) { $State = 'queued'; $Text = 'ждёт свободной памяти' }
            else { $State = 'queued'; $Text = 'в очереди на запуск' }
        }
    }
    return [ordered]@{
        key = [string]$Account.Key; userId = [string]$Account.UserId; name = [string]$Account.Name; main = [bool]$Account.IsMain
        state = $State; text = $Text; pid = $windowPid; memGB = $memoryGB; wsGB = $wsGB; capMB = $capMB; startedAt = Format-Utc $started
        script = $scriptState; restarts = [int]$Account.Restarts
        lastReason = $Account.LastReason; lastReasonAt = Format-Utc $Account.LastReasonAt
        fails = [int]$Account.Fails; failTotal = [int]$Account.FailTotal; joinOk = [int]$Account.JoinOk
        lastError = $Account.LastError; lastErrorAt = Format-Utc $Account.LastErrorAt
        userOpen = [bool]($tracker -and $tracker.UserOpen); fps = [int](Get-AccountFps $Account)
    }
}

# Состояние для приложения — раз в секунду (с -Force — сразу). Ошибка здесь не должна ронять движок: окна важнее
# картинки в приложении (19.10 первая версия падала на старте из-за переполнения в Get-AccountView).
function Write-AppStatus([switch]$Force) {
    if (-not $script:Out) { return }
    $now = Get-Date
    if (-not $Force -and ($now - $script:StatusAt).TotalMilliseconds -lt 900) { return }
    $script:StatusAt = $now
    try { Send-AppStatus $now }
    catch {
        if (-not $script:StatusErrorLogged) {
            $script:StatusErrorLogged = $true
            Write-Status "состояние для приложения не собралось: $($_.Exception.Message)" DarkYellow
        }
    }
}

function Send-AppStatus([datetime]$now) {
    if ($script:RebootAt -and $now -gt $script:RebootAt.AddSeconds(30)) { $script:RebootAt = $null }   # перезагрузку отменили не через нас

    $accounts = @()
    foreach ($account in @($script:Accounts)) { $accounts += Get-AccountView $account }
    foreach ($item in @($script:Deferred)) {
        $note = if ($item.PSObject.Properties['Note'] -and $item.Note) { $item.Note } else { 'Роблокс не пускает аккаунт — проверю позже' }
        $accounts += Get-AccountView $item.Account 'deferred' $note
    }
    foreach ($key in @($script:Problems.Keys)) { $accounts += [ordered]@{ key = [string]$key; state = 'problem'; text = $script:Problems[$key].Text } }
    if ($null -eq $script:TotalMemoryGB) { $script:TotalMemoryGB = try { (Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize / 1MB } catch { 0 } }
    $free = Get-FreeMemoryGB

    Send-App ([ordered]@{
        t = 'status'; phase = $script:Phase; at = Format-Utc $now
        accounts = $accounts
        freeGB = if ($null -ne $free) { [Math]::Round($free, 1) } else { $null }
        ramGB = [Math]::Round($script:TotalMemoryGB, 1)
        potassium = [ordered]@{
            executor = $Executor; name = $script:ExecName; dir = if ($Executor -ne 'potassium') { $script:ExecDir } else { $null }
            enabled = -not $NoPotassium; running = if ($Executor -ne 'potassium') { Test-RealRunning } else { [bool](Get-Process -Name Potassium -ErrorAction SilentlyContinue) }
            signedOut = [bool]$script:PotassiumSignedOutSince; clients = $script:PotassiumClients; autoAttachProblem = [bool]$script:AutoAttachWarned
        }
        launchBlocked = [bool]$script:LaunchBlocked
        rebootAt = Format-Utc $script:RebootAt
        schedule = [ordered]@{
            enabled = [bool]$script:Schedule.Enabled; action = $script:Schedule.Action; time = $script:Schedule.Time
            next = Format-Utc $script:ScheduleNextUtc; last = Format-Utc $script:ScheduleLast
        }
    })
}

# Канал к приложению: stdout (строки JSON, UTF-8 без BOM) и чтение команд из stdin.
function Open-AppChannel {
    try {
        $script:Out = New-Object IO.StreamWriter([Console]::OpenStandardOutput(), (New-Object Text.UTF8Encoding $false))
        $script:Out.AutoFlush = $true
    } catch { $script:Out = $null }
    [RejoinInput]::Start()
}

#endregion

function Start-Watchdog {
    # Вторая копия (или старый AutoRejoin в окне консоли) перезапускала бы те же окна наперегонки с первой.
    $isNew = $false
    $script:InstanceLock = New-Object Threading.Mutex($true, $InstanceName, [ref]$isNew)
    if (-not $isNew) {
        Write-Status 'AutoRejoin уже запущен (другая копия или старая версия в окне консоли) — второй не нужен.' Yellow
        $script:ExitCode = 4
        return
    }
    try { Set-Content -LiteralPath $PidFile -Value $PID -Encoding ASCII } catch {}

    try {
        if ((Get-Item -LiteralPath $OwnLog -ErrorAction Stop).Length -gt 5MB) {
            Move-Item -LiteralPath $OwnLog (Join-Path $DataDir 'rejoin.old.log') -Force
        }
    } catch {}

    # Настройки — первым делом: с этого момента действует расписание (см. Update-Schedule).
    Update-AppConfig -Force
    $script:ScheduleLast = Read-ScheduleStamp
    Write-Status "AutoRejoin запущен. $(Get-ScheduleText)" Cyan
    Write-AppStatus -Force

    if ($StartupDelay -gt 0) {
        $script:Phase = 'delay'
        Write-Status "Автозапуск: жду $StartupDelay сек, пока прогрузится Windows..." Cyan
        if (Wait-Interruptible $StartupDelay) { return }
    }

    try { $vault = Read-Vault }
    catch {
        Write-Status "Не читается список аккаунтов (vault.dat): $($_.Exception.Message)" Red
        $script:ExitCode = 3
        return
    }
    $script:VaultStamp = Get-FileStamp $VaultFile
    $script:VaultPotassium = $vault.potassium
    $entries = Get-VaultEntries $vault
    if (-not $entries) {
        Write-Status 'Нет ни одного включённого аккаунта — добавь аккаунт в приложении и нажми «Запустить».' Yellow
        $script:ExitCode = 3
        return
    }

    Enable-MultiInstance
    # Потассиум открываем сразу: он грузится, пока ждём интернет и проверяем куки (раньше на это уходили отдельные 3 сек).
    Initialize-Potassium -NoWait
    $script:Phase = 'network'
    Write-AppStatus -Force
    if (-not (Wait-ForNetwork)) { return }

    # Мейн — первым и сразу: проверили его куку — запускаем его окно, остальные проверяем, пока оно грузится
    # (user 21.10: "мейн должен зайти моментально, пофиг на другие аккаунты").
    $script:Phase = 'accounts'
    Write-AppStatus -Force
    $sorted = @(@($entries | Where-Object { $_.main }) + @($entries | Where-Object { -not $_.main }))
    $mainLaunched = $false
    foreach ($entry in $sorted) {
        if ($script:StopRequested) { return }
        try { $account = New-VaultAccount $entry }
        catch {
            $script:Problems[[string]$entry.id] = [pscustomobject]@{ Text = "своя ссылка не подходит: $($_.Exception.Message)"; Cookie = Get-CleanCookie $entry.cookie; Link = Get-EntryOwnLink $entry }
            Write-Status "аккаунт $($entry.name): своя ссылка не подходит — $($_.Exception.Message)" Red
            continue
        }
        Add-AccountChecked $account -Retry
        # Кука мейна не подошла, а вход у него по паролю — приложение берёт новую; ждём её, чтобы мейн зашёл первым.
        if ($account.IsMain -and -not ($script:Accounts -contains $account)) {
            $fresh = Wait-AppCookie $account 20
            if ($fresh) {
                $script:Problems.Remove($account.Key)
                $script:Deferred = @($script:Deferred | Where-Object { $_.Account.Key -ne $account.Key })
                try { $account = New-VaultAccount $fresh; Add-AccountChecked $account -Retry } catch { Write-Status "ошибка: $($_.Exception.Message)" DarkYellow $account }
            }
        }
        Write-AppStatus
        if (-not $mainLaunched -and $account.IsMain -and ($script:Accounts -contains $account)) {
            $mainLaunched = $true
            Wait-PotassiumWarmup
            # Потассиум показал экран входа — войти до окна мейна, иначе оно останется без скрипта.
            try { Update-PotassiumState } catch {}
            # Окно мейна могло остаться открытым (движок перезапустили без перезагрузки) — тогда его просто подхватит цикл.
            try { Update-Trackers } catch {}
            try { Step-Account $account } catch { Write-Status "ошибка: $($_.Exception.Message)" DarkYellow $account }
        }
    }
    if ($script:StopRequested) { return }

    Test-MemoryCapacity
    Remove-AntiAfk
    if ($Executor -eq 'potassium' -and -not $NoPotassium) { Install-RealInjectMarker }
    $script:Phase = 'potassium'
    Write-AppStatus -Force
    if ($script:PotassiumStartedAt) { Wait-PotassiumWarmup }
    elseif (-not $NoPotassium -and -not (Get-Process -Name Potassium -ErrorAction SilentlyContinue)) { Initialize-Potassium }
    # Скрипт в окна подключает автоаттач Потассиума — до запуска первого окна проверяем, что он включён
    # (первые попытки могут не пройти, пока Потассиум рисует своё окно).
    if (-not $NoPotassium -and ($Executor -ne 'potassium' -or (Get-Process -Name Potassium -ErrorAction SilentlyContinue))) {
        for ($try = 0; $try -lt 5 -and -not (Confirm-PotassiumAutoAttachOn -Now); $try++) { if (Wait-Interruptible 2) { return } }
    }
    $script:AutoAttachCheckAt = Get-Date
    $script:Phase = 'running'
    $order = if ($ScriptStartTimeout -gt 0 -and -not $NoPotassium) { 'по одному: следующее окно — когда в предыдущем запустился скрипт' } else { 'по очереди' }
    Write-Status "Захожу в игру всеми аккаунтами ($order) и слежу за окнами." Cyan
    if (-not $script:Accounts -and -not $script:Deferred) {
        Write-Status 'Ни один аккаунт не прошёл проверку — поправь куки в приложении, движок подхватит их сам.' Red
    }

    while (-not $script:StopRequested) {
        Invoke-AppCommands
        if ($script:StopRequested) { break }
        try { Update-AppConfig } catch { Write-Status "настройки приложения: $($_.Exception.Message)" DarkYellow }
        try { Update-Schedule } catch { Write-Status "расписание: $($_.Exception.Message)" DarkYellow }
        # Случайная ошибка в одном цикле (окно закрылось посреди проверки и т.п.) не должна останавливать скрипт.
        try { Update-Trackers } catch { Write-Status "проверка окон: $($_.Exception.Message)" DarkYellow }
        try { Update-DisconnectMarks } catch { Write-Status "проверка вылетов из игры: $($_.Exception.Message)" DarkYellow }
        try { Find-WerCrashes } catch { Write-Status "проверка падений Роблокса: $($_.Exception.Message)" DarkYellow }
        try { Close-ConnectivityDialogs } catch { Write-Status "проверка окон Froststrap: $($_.Exception.Message)" DarkYellow }
        try { Update-PotassiumState } catch { Write-Status "проверка Потассиума: $($_.Exception.Message)" DarkYellow }
        try { Test-LowMemory; Write-MemoryEvents; Restart-PotassiumIfDead }
        catch { Write-Status "проверка памяти/Потассиума: $($_.Exception.Message)" DarkYellow }
        foreach ($account in (Get-AccountsMainFirst)) {
            try { Step-Account $account } catch { Write-Status "ошибка: $($_.Exception.Message)" DarkYellow $account }
        }
        # После Step-Account: скрипт в окнах уже проверен на этом круге — Attach не жмётся окну, где он только что запустился.
        try { Invoke-ManualAttach } catch { Write-Status "Attach в Потассиуме: $($_.Exception.Message)" DarkYellow }
        try { $null = Confirm-PotassiumAutoAttachOn } catch { Write-Status "проверка автоаттача Потассиума: $($_.Exception.Message)" DarkYellow }
        if ($AutoReboot -and $script:LaunchBlocked -and $script:RebootRetryAt -and (Get-Date) -ge $script:RebootRetryAt) {
            $script:RebootRetryAt = $null
            try { Invoke-AutoReboot } catch {}
        }
        try { Update-DeferredAccounts } catch { Write-Status "проверка отложенных аккаунтов: $($_.Exception.Message)" DarkYellow }
        try { Update-DefaultLink } catch { Write-Status "проверка ссылки на сервер: $($_.Exception.Message)" DarkYellow }
        # Роблокс (эксперимент "Tray treatment") ставит каждому окну значок в трее, и от закрытых принудительно окон
        # значки оставались: 10.10 под стрелочкой накапливалось ~200 штук. Чистим через 2 сек после закрытия окна
        # (процесс уже точно завершён) и на всякий случай раз в 5 минут.
        try {
            $now = Get-Date
            if (($script:TrayDirtyAt -and ($now - $script:TrayDirtyAt).TotalSeconds -ge 2) -or ($now - $script:TraySweptAt).TotalMinutes -ge 5) {
                [RejoinWindows]::ClearGhostTrayIcons()
                $script:TrayDirtyAt = $null
                $script:TraySweptAt = $now
            }
        } catch {}

        # Пока окно запускается, грузится или ждёт скрипт — проверяем раз в секунду, иначе раз в $CheckInterval сек.
        $sleep = [double]$CheckInterval
        $inFlight = $script:Accounts | Where-Object {
            $_.LaunchedAt -or ($_.Tracker -and (-not $_.Tracker.EverJoined -or (Test-WaitScript $_.Tracker)))
        }
        if ($inFlight) { $sleep = [Math]::Min($sleep, 1.0) }
        # Окно ждёт перезапуска — просыпаемся точно к сроку запуска, а не через полные $CheckInterval сек.
        if (-not $script:LaunchBlocked) {
            foreach ($account in @($script:Accounts | Where-Object { -not $_.Tracker -and -not $_.LaunchedAt })) {
                $due = ($account.NextLaunch - (Get-Date)).TotalSeconds
                # Срок прошёл, а окно всё не запущено — его что-то держит (ждёт Attach, памяти и т.п.): раз в секунду хватит.
                if ($due -le -1) { $sleep = [Math]::Min($sleep, 1.0) }
                elseif ($due -lt $sleep) { $sleep = [Math]::Max($due, 0.2) }
            }
        }
        try { Write-AppStatus } catch {}
        Wait-Loop $sleep
    }
    # Без движка потолок никто не подстраивает — окну, которому станет тесно, его некому поднять: снимаем.
    try { [RejoinMemory]::ReleaseAll() } catch {}
    $script:Phase = 'stopped'
    Write-Status 'AutoRejoin остановлен: окна Роблокса остаются открытыми, но больше не перезаходят.' Cyan
}

# При подключении через ". .\engine.ps1" (для проверки функций) цикл не запускается.
if ($MyInvocation.InvocationName -ne '.') {
    if ($SetupPagefile) {
        # С правами администратора Set-Pagefile перезапускает движок отдельным скрытым процессом (-Elevated): ему канал не нужен.
        if (-not $Elevated) { Open-AppChannel }
        try { $null = Set-EngineConfig (Get-Content -LiteralPath $ConfigFile -Raw -Encoding UTF8 | ConvertFrom-Json) } catch {}
        exit (Set-Pagefile)
    }
    Open-AppChannel
    Start-Watchdog
    $script:Phase = if ($script:StopRequested -or $script:ExitCode) { 'stopped' } else { $script:Phase }
    try { Write-AppStatus -Force } catch {}
    exit $script:ExitCode
}
