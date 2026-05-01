import sys
import os
import re
import tkinter as tk
from tkinter import filedialog
import winreg
import pystray
from PIL import Image, ImageDraw

APP_NAME = "EqAPO Tray"
CONFIG_PATH_FILE = os.path.join(os.path.dirname(os.path.abspath(sys.argv[0])), "eqapo_config_path.txt")

# ── 配置文件路径的读写 ──────────────────────────────────────────────────────

def load_config_path():
    if os.path.exists(CONFIG_PATH_FILE):
        with open(CONFIG_PATH_FILE, "r", encoding="utf-8") as f:
            path = f.read().strip()
            if os.path.exists(path):
                return path
    return r"C:\Program Files\EqualizerAPO\config\config.txt"

def save_config_path(path):
    with open(CONFIG_PATH_FILE, "w", encoding="utf-8") as f:
        f.write(path)

# ── Preamp 读写 ─────────────────────────────────────────────────────────────

def read_preamp(config_path):
    if not os.path.exists(config_path):
        return 0.0
    with open(config_path, "r", encoding="utf-8") as f:
        content = f.read()
    m = re.search(r"Preamp:\s*([-\d.]+)\s*dB", content)
    return float(m.group(1)) if m else 0.0

def write_preamp(config_path, value):
    if not os.path.exists(config_path):
        return
    with open(config_path, "r", encoding="utf-8") as f:
        content = f.read()
    new_line = f"Preamp: {value:.1f} dB"
    if re.search(r"Preamp:\s*[-\d.]+\s*dB", content):
        content = re.sub(r"Preamp:\s*[-\d.]+\s*dB", new_line, content)
    else:
        content = new_line + "\n" + content
    with open(config_path, "w", encoding="utf-8") as f:
        f.write(content)

# ── 开机自启（写注册表） ────────────────────────────────────────────────────

def get_startup():
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                             r"Software\Microsoft\Windows\CurrentVersion\Run",
                             0, winreg.KEY_READ)
        winreg.QueryValueEx(key, APP_NAME)
        winreg.CloseKey(key)
        return True
    except FileNotFoundError:
        return False

def set_startup(enable):
    key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                         r"Software\Microsoft\Windows\CurrentVersion\Run",
                         0, winreg.KEY_SET_VALUE)
    if enable:
        exe = os.path.abspath(sys.argv[0])
        winreg.SetValueEx(key, APP_NAME, 0, winreg.REG_SZ, f'"{exe}"')
    else:
        try:
            winreg.DeleteValue(key, APP_NAME)
        except FileNotFoundError:
            pass
    winreg.CloseKey(key)

# ── 托盘图标 ────────────────────────────────────────────────────────────────

def create_tray_icon():
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([2, 2, 62, 62], fill=(30, 144, 255))
    d.text((14, 20), "dB", fill="white")
    return img

# ── 主程序 ──────────────────────────────────────────────────────────────────

class App:
    def __init__(self):
        self.config_path = load_config_path()
        self.window = None

    def show_window(self, icon=None, item=None):
        if self.window and self.window.winfo_exists():
            self.window.lift()
            return

        self.window = tk.Tk()
        self.window.title("EqAPO 增益控制")
        self.window.resizable(False, False)
        self.window.attributes("-topmost", True)

        # 配置文件选择
        tk.Label(self.window, text="配置文件:").grid(row=0, column=0, padx=10, pady=10, sticky="w")
        self.path_var = tk.StringVar(value=self.config_path)
        tk.Entry(self.window, textvariable=self.path_var, width=42, state="readonly").grid(row=0, column=1, padx=4)
        tk.Button(self.window, text="浏览", command=self.browse_config).grid(row=0, column=2, padx=10)

        # 增益滑条
        current = read_preamp(self.config_path)
        tk.Label(self.window, text="增益 (dB):").grid(row=1, column=0, padx=10, pady=10, sticky="w")
        self.gain_var = tk.DoubleVar(value=current)
        self.val_label = tk.Label(self.window, text=f"{current:+.1f} dB", width=9)
        self.val_label.grid(row=1, column=2, padx=10)
        tk.Scale(self.window, from_=-20, to=20, resolution=0.5,
                 orient=tk.HORIZONTAL, variable=self.gain_var,
                 length=320, showvalue=False,
                 command=self.on_slider).grid(row=1, column=1, padx=4)

        # 开机自启
        self.startup_var = tk.BooleanVar(value=get_startup())
        tk.Checkbutton(self.window, text="开机自启动",
                       variable=self.startup_var,
                       command=lambda: set_startup(self.startup_var.get())
                       ).grid(row=2, column=1, pady=8)

        self.window.protocol("WM_DELETE_WINDOW", self.hide_window)
        self.window.mainloop()

    def on_slider(self, val):
        v = float(val)
        self.val_label.config(text=f"{v:+.1f} dB")
        write_preamp(self.config_path, v)

    def browse_config(self):
        path = filedialog.askopenfilename(
            title="选择 Equalizer APO 配置文件",
            filetypes=[("Text files", "*.txt"), ("All files", "*.*")]
        )
        if path:
            self.config_path = path
            self.path_var.set(path)
            save_config_path(path)
            v = read_preamp(path)
            self.gain_var.set(v)
            self.val_label.config(text=f"{v:+.1f} dB")

    def hide_window(self):
        self.window.destroy()
        self.window = None

    def quit_app(self, icon, item):
        icon.stop()

    def run(self):
        menu = pystray.Menu(
            pystray.MenuItem("打开控制面板", self.show_window, default=True),
            pystray.MenuItem("退出", self.quit_app),
        )
        icon = pystray.Icon(APP_NAME, create_tray_icon(), APP_NAME, menu)
        icon.run()

if __name__ == "__main__":
    App().run()
    
# ── 生成可执行文件exe命令
# pyinstaller --onefile --windowed --name eqapo-tray eqapo_tray.py