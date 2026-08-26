; ========================================================
; Rino Inno Setup 现代定制安装包配置文件 (Rino 暖白品牌主题版)
; ========================================================
; 1. 采用 Windows 原生 AnimateWindow 硬件加速接口，实现极速无卡顿的窗口淡入淡出动画。
; 2. Rino 品牌暖白卡片：背景设为 Rino 标志性的暖白底色 ($F0F4FF / #FFF4F0)。
; 3. Rino 暖杏橙胶囊主按钮：采用 TBitmapImage 加载本地 GDI+ 高抗锯齿技术的 Rino 暖杏橙 ($4FB4FF / #FFB44F)、
;    纯白加粗汉字 ($FFFFFF)，完美契合 Rino 视觉规范。
; 4. 优雅弹窗路径选择：点击“立即安装”时触发系统级 BrowseForFolder 路径选择弹窗，
;    既保留了极简无边框设计格调，又赋予了用户 100% 的自定义安装位置权利。
; 5. 自动桌面快捷方式优化：配置 WorkingDir 为 {app}，确保快捷方式启动时的工作路径绝对正确。
; 6. 采用纯 ASCII 十进制 Unicode 字符编码拼接，100% 根除乱码。
; 7. 屏蔽系统底层所有向导按钮，无边框 18px 窗体圆角裁切全部保留。

[Setup]
AppName=Rino
AppVersion=0.1.0
AppPublisher=SweetFox
DefaultDirName={localappdata}\Rino
DefaultGroupName=Rino
OutputBaseFilename=Rino_0.1.0_Setup
Compression=lzma2/max
SolidCompression=yes
OutputDir=..
UninstallDisplayIcon={app}\Rino.exe
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
DisableWelcomePage=yes
PrivilegesRequired=lowest
SetupIconFile=..\logo.ico

[Messages]
ConfirmUninstall=您确定要完全卸载 Rino 吗？
UninstallAppFullTitle=卸载 Rino
UninstallAppTitle=卸载 Rino
UninstallStatusLabel=正在从您的电脑中安全移除 Rino，请稍候...
ButtonYes=是(&Y)
ButtonNo=否(&N)
ExitSetupMessage=安装尚未完成。如果您现在退出，将不会安装 Rino。#13#10#13#10您确定要退出安装程序吗？
ExitSetupTitle=退出安装

[Files]
Source: "..\distribution\output\Rino\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "btn_border.bmp"; Flags: dontcopy

[Icons]
Name: "{group}\Rino"; Filename: "{app}\Rino.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Rino"; Filename: "{app}\Rino.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\Rino.exe"; Description: "运行 Rino"; Flags: nowait postinstall skipifsilent

[Code]
const
  WM_SYSCOMMAND = $0112;
  AW_BLEND = $00080000;
  AW_ACTIVATE = $00020000;
  AW_HIDE = $00010000;

var
  // 界面控件
  CloseBtnLabel: TLabel;
  TitleLabel: TLabel;
  SubtitleLabel: TLabel;
  DragHandle: TLabel;
  
  // 立即安装按钮背景图和文本标签
  InstallBtn: TBitmapImage;
  InstallBtnLabel: TLabel;
  StatusLabel: TLabel;

// ==========================================
// 外部 Windows API 声明
// ==========================================
procedure ReleaseCapture; external 'ReleaseCapture@user32.dll stdcall';
procedure SendMessage(hWnd: HWND; Msg: Longint; wParam: Longint; lParam: Longint); external 'SendMessageW@user32.dll stdcall';
function AnimateWindow(hWnd: HWND; dwTime: Longint; dwFlags: Longint): BOOL; external 'AnimateWindow@user32.dll stdcall';
function CreateRoundRectRgn(nLeftRect, nTopRect, nRightRect, nBottomRect, nWidthEllipse, nHeightEllipse: Integer): LongWord; external 'CreateRoundRectRgn@gdi32.dll stdcall';
function SetWindowRgn(hWnd: HWND; hRgn: LongWord; bRedraw: Boolean): Integer; external 'SetWindowRgn@user32.dll stdcall';

// ==========================================
// 窗体无边框拖拽支持
// ==========================================
procedure FormMouseDown(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  if Button = mbLeft then
  begin
    ReleaseCapture;
    SendMessage(WizardForm.Handle, WM_SYSCOMMAND, $F012, 0);
  end;
end;

// ==========================================
// 退出动作
// ==========================================
procedure CloseBtnClick(Sender: TObject);
begin
  // 淡出退出
  AnimateWindow(WizardForm.Handle, 250, AW_BLEND or AW_HIDE);
  WizardForm.Close;
end;

// ==========================================
// 立即安装触发流程 (一键极速安装)
// ==========================================
procedure StartInstall(Sender: TObject);
var
  SelectedDir: string;
begin
  SelectedDir := ExpandConstant('{localappdata}\Rino'); // 获取默认推荐路径

  // 1. 弹出系统原生文件夹选择弹窗让用户自由选择路径
  // 转义提示文字：“请选择 Rino 的安装目标文件夹：”
  if BrowseForFolder(#35831#36873#25321#32#82#105#110#111#32#30340#23433#35013#30446#26631#25991#20214#22841#65306, SelectedDir, True) then
  begin
    // 将选择好的路径赋予向导引擎的默认安装路径
    WizardForm.DirEdit.Text := SelectedDir;

    // 1.1 隐藏欢迎界面的安装按钮和文本
    InstallBtn.Hide;
    InstallBtnLabel.Hide;
    
    // 2. 切换并显示状态与极细进度条 (转义：“正在写入文件，请稍候...”)
    StatusLabel.Caption := #27491#22312#20889#20837#25991#20214#65292#35831#31245#20505#46#46#46;
    StatusLabel.Show;
    
    // 3. 将系统原生的 ProgressGauge 的 Parent 更改为 WizardForm 顶层，并修改布局
    WizardForm.ProgressGauge.Parent := WizardForm;
    WizardForm.ProgressGauge.Left := 80;
    WizardForm.ProgressGauge.Top := 315;
    WizardForm.ProgressGauge.Width := 590;
    WizardForm.ProgressGauge.Height := 5; // 变成极细进度条
    WizardForm.ProgressGauge.Show;
    
    // 4. 后台模拟点击 NextButton 跨过向导，直达安装复制步骤
    WizardForm.NextButton.OnClick(WizardForm);
  end;
end;

// ==========================================
// 初始化向导接管与重绘
// ==========================================
procedure InitializeWizard();
var
  Rgn: LongWord;
  BtnBorderFileName: string;
begin
  // 1. 去除系统标题栏与边框，定义宽大舒适的 750x500 尺寸
  WizardForm.BorderStyle := bsNone;
  WizardForm.Width := 750;
  WizardForm.Height := 500;
  WizardForm.Color := $F0F4FF; // 设为 Rino 标志性暖白背景 (RGB: #FFF4F0 -> BGR: $F0F4FF)
  
  // 1.1 裁切 18px 精致圆角
  Rgn := CreateRoundRectRgn(0, 0, 750, 500, 18, 18);
  SetWindowRgn(WizardForm.Handle, Rgn, True);
  
  // 2. 强力隐藏并移出 Inno Setup 默认按钮和面板区，以防它们在安装进行中被底层重新显示
  WizardForm.BackButton.Hide;
  WizardForm.BackButton.Left := 9999;
  WizardForm.BackButton.Top := 9999;
  
  WizardForm.NextButton.Hide;
  WizardForm.NextButton.Left := 9999;
  WizardForm.NextButton.Top := 9999;
  
  WizardForm.CancelButton.Hide;
  WizardForm.CancelButton.Left := 9999;
  WizardForm.CancelButton.Top := 9999;
  
  WizardForm.Bevel.Hide;
  WizardForm.PageNameLabel.Hide;
  WizardForm.PageDescriptionLabel.Hide;
  WizardForm.OuterNotebook.Hide;
  
  // 3. 创建位于顶部的隐形拖拽热区 (宽 750, 高 65)
  DragHandle := TLabel.Create(WizardForm);
  DragHandle.Parent := WizardForm;
  DragHandle.Left := 0;
  DragHandle.Top := 0;
  DragHandle.Width := 750;
  DragHandle.Height := 65;
  DragHandle.Transparent := True;
  DragHandle.Caption := '';
  DragHandle.OnMouseDown := @FormMouseDown;
  
  // 4. 右上角放置极简关闭按钮 (对齐右边距 715)
  CloseBtnLabel := TLabel.Create(WizardForm);
  CloseBtnLabel.Parent := WizardForm;
  CloseBtnLabel.Caption := #10005; // ✕
  CloseBtnLabel.Font.Name := 'Segoe UI';
  CloseBtnLabel.Font.Size := 12;
  CloseBtnLabel.Font.Color := $938E8E; // Rino 辅助灰色 (#8E8E93)
  CloseBtnLabel.Left := 715;
  CloseBtnLabel.Top := 20;
  CloseBtnLabel.Cursor := crHand;
  CloseBtnLabel.OnClick := @CloseBtnClick;
  
  // 5. 写入软件名称 (大字号，SF Pro / 微软雅黑)
  TitleLabel := TLabel.Create(WizardForm);
  TitleLabel.Parent := WizardForm;
  TitleLabel.Caption := 'Rino';
  TitleLabel.Font.Name := 'SF Pro';
  TitleLabel.Font.Size := 32;
  TitleLabel.Font.Style := [fsBold];
  TitleLabel.Font.Color := $383636; // Rino 主文本炭黑深色 (#363638)
  TitleLabel.Left := 80;
  TitleLabel.Top := 115;
  TitleLabel.Transparent := True;
  TitleLabel.OnMouseDown := @FormMouseDown;

  // 5.1 写入副标题 / 描述
  SubtitleLabel := TLabel.Create(WizardForm);
  SubtitleLabel.Parent := WizardForm;
  SubtitleLabel.Caption := 'MaaFramework Desktop';
  SubtitleLabel.Font.Name := 'Segoe UI';
  SubtitleLabel.Font.Size := 10;
  SubtitleLabel.Font.Color := $938E8E; // Rino 辅助浅灰 (#8E8E93)
  SubtitleLabel.Left := 82;
  SubtitleLabel.Top := 168;
  SubtitleLabel.Transparent := True;
  SubtitleLabel.OnMouseDown := @FormMouseDown;
  
  // 6. 加载自定义暖杏橙圆角 Pill 背景
  ExtractTemporaryFile('btn_border.bmp');
  BtnBorderFileName := ExpandConstant('{tmp}\btn_border.bmp');
  
  InstallBtn := TBitmapImage.Create(WizardForm);
  InstallBtn.Parent := WizardForm;
  InstallBtn.Width := 162;
  InstallBtn.Height := 44;
  InstallBtn.Left := 80;
  InstallBtn.Top := 290;
  InstallBtn.Bitmap.LoadFromFile(BtnBorderFileName);
  InstallBtn.Cursor := crHand;
  InstallBtn.OnClick := @StartInstall;
  
  // 7. 创建“立即安装”文本标签，挂在 WizardForm 上以重叠于背景图中央
  InstallBtnLabel := TLabel.Create(WizardForm);
  InstallBtnLabel.Parent := WizardForm;
  InstallBtnLabel.AutoSize := False;
  InstallBtnLabel.Width := 162;
  InstallBtnLabel.Height := 32;
  InstallBtnLabel.Left := 80;            // 与 InstallBtn 左边距完全对齐
  InstallBtnLabel.Top := 296;            // 高度由 22 扩展到 32，Top 设为 296 以提供充裕绘图区防止截断并完美居中
  InstallBtnLabel.Alignment := taCenter; // 启用 VCL 引擎原生水平绝对居中，杜绝任何 DPI 偏移
  InstallBtnLabel.Caption := #31435#21363#23433#35013; // 转义：“立即安装”，彻底杜绝乱码
  InstallBtnLabel.Font.Name := 'Microsoft YaHei';
  InstallBtnLabel.Font.Size := 11;
  InstallBtnLabel.Font.Style := [fsBold];
  InstallBtnLabel.Font.Color := $FFFFFF; // 纯白色加粗字体，在暖杏橙背景上醒目清晰
  InstallBtnLabel.Transparent := True;  // 100% 透明，完全露出底层描边图
  InstallBtnLabel.Cursor := crHand;
  InstallBtnLabel.OnClick := @StartInstall;
  
  // 8. 创建隐藏的状态控件
  StatusLabel := TLabel.Create(WizardForm);
  StatusLabel.Parent := WizardForm;
  StatusLabel.Caption := #20934#22791#20013#46#46#46; // 转义：“准备中...”
  StatusLabel.Font.Name := 'Microsoft YaHei';
  StatusLabel.Font.Size := 9;
  StatusLabel.Font.Color := $666363; // Rino 次级文本色 (#636366)
  StatusLabel.Left := 80;
  StatusLabel.Top := 285;
  StatusLabel.Transparent := True;
  StatusLabel.Hide;
end;

// ==========================================
// 监测步骤变化以执行淡出退出
// ==========================================
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // 转义：“安装完成！启动中...”
    StatusLabel.Caption := #23433#35013#23436#25104#65281#21551#21160#20013#46#46#46;
    
    // 稍微延迟，展示 100% 满格状态
    Sleep(350);
    
    // 播放系统淡出动画并关闭窗体
    AnimateWindow(WizardForm.Handle, 300, AW_BLEND or AW_HIDE);
  end;
end;

// ==========================================
// 页面变动时执行淡入动画
// ==========================================
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
  begin
    // 首次进入欢迎页时，播放硬件加速淡入
    AnimateWindow(WizardForm.Handle, 400, AW_BLEND or AW_ACTIVATE);
  end;
end;

