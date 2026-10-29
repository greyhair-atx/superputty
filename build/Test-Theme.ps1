param([ValidateSet('Light','Dark','Dark Modern','System')][string]$Theme = 'Dark Modern')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repoRoot 'artifacts/theme-tests'
New-Item -ItemType Directory -Force (Join-Path $root 'results') | Out-Null
$previousProfile = $env:USERPROFILE
$previousLocation = Get-Location
try {
    $env:USERPROFILE = Join-Path $root ('profile-' + $Theme)
    New-Item -ItemType Directory -Force $env:USERPROFILE | Out-Null
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $app = Join-Path $repoRoot 'bin/x64/Release'
    Set-Location $app
    foreach ($name in @('log4net.dll','WeifenLuo.WinFormsUI.Docking.dll','WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll','SuperPutty.exe')) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $app $name))
    }
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $app 'SuperPutty.exe'))
    $settingsType = $assembly.GetType('SuperPutty.Properties.Settings')
    $settings = $settingsType.GetProperty('Default').GetValue($null, $null)
    $settings.Theme = $Theme
    $settings.AutoUpdateCheck = $false
    $settings.SettingsFolder = $env:USERPROFILE
    $settings.PuttyExe = 'C:\Windows\System32\cmd.exe'
    # Match the ordering that previously risked creating a control before text rendering was configured.
    $icons = [SuperPutty.SuperPuTTY]::LoadImageList('default', $false)
    [Windows.Forms.Application]::EnableVisualStyles()
    [Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
    "Theme=$Theme IsDark=$([SuperPutty.Gui.ThemeManager]::IsDark)"
    if ([SuperPutty.Gui.ThemeManager]::RequiresRestart($Theme)) { throw 'Unchanged theme must not require restart' }
    if ($Theme -eq 'Dark Modern' -and ![SuperPutty.Gui.ThemeManager]::RequiresRestart('Dark')) { throw 'Switching dark styles must require restart' }
    if ($Theme -eq 'Dark' -and ![SuperPutty.Gui.ThemeManager]::RequiresRestart('Dark Modern')) { throw 'Switching dark styles must require restart' }
    $main = New-Object SuperPutty.frmSuperPutty
    $null = $main.Handle
    $dock = $main.GetType().GetProperty('DockPanel', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($main,$null)
    "Dock theme=$($dock.Theme.GetType().Name)"
    foreach ($typeName in @('SuperPutty.dlgFindPutty','SuperPutty.AboutBox1','SuperPutty.dlgScriptEditor','SuperPutty.Gui.QuickSelector','SuperPutty.Scp.PscpBrowserPanel')) {
        $form = [Activator]::CreateInstance($assembly.GetType($typeName))
        # Exercise Load and handle recreation without opening network sessions or changing saved settings.
        $null = $form.Handle
        $form.GetType().GetMethod('OnLoad',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($form, @([EventArgs]::Empty)) | Out-Null
        "Created $typeName back=$($form.BackColor)"
        if ($Theme -eq 'Dark Modern' -and $typeName -eq 'SuperPutty.dlgFindPutty') {
            $form.TopMost = $true
            $form.Show()
            [Threading.Thread]::Sleep(500)
            [Windows.Forms.Application]::DoEvents()
            $bitmap = New-Object Drawing.Bitmap -ArgumentList $form.Width,$form.Height
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $graphics.CopyFromScreen($form.Location, [Drawing.Point]::Empty, $form.Size)
            $graphics.Dispose()
            $bitmap.Save((Join-Path $root 'results/options-modern.png'))
            $bitmap.Dispose()
            $form.Hide()
        }
        if ($typeName -eq 'SuperPutty.Gui.QuickSelector') {
            $data = New-Object SuperPutty.Gui.QuickSelectorData
            $null = $data.ItemData.AddItemDataRow('SSH','SSH',[Drawing.Color]::Black,$null)
            $null = $data.ItemData.AddItemDataRow('MinTTY','MinTTY',[Drawing.Color]::Blue,$null)
            $grid = $form.Controls.Find('dataGridViewData',$true)[0]
            $grid.DataSource = $data.ItemData.DefaultView
            $handler = $form.GetType().GetMethod('dataGridViewData_CellFormatting',[Reflection.BindingFlags]'Instance,NonPublic')
            foreach ($rowIndex in @(0,1)) {
                $style = New-Object Windows.Forms.DataGridViewCellStyle
                $formatEvent = New-Object Windows.Forms.DataGridViewCellFormattingEventArgs -ArgumentList 0,$rowIndex,'SSH',([string]),$style
                $null = $handler.Invoke($form,@($grid,$formatEvent.PSObject.BaseObject))
                $expected = if ([SuperPutty.Gui.ThemeManager]::IsDark) {
                    if ($rowIndex -eq 0) { $grid.DefaultCellStyle.ForeColor } else { [Drawing.Color]::FromArgb(86,156,214) }
                } else {
                    if ($rowIndex -eq 0) { [Drawing.Color]::Black } else { [Drawing.Color]::Blue }
                }
                if ($formatEvent.CellStyle.ForeColor.ToArgb() -ne $expected.ToArgb()) { throw "Unreadable selector row $rowIndex" }
            }
            'PASS selector row contrast'
        }
        if ($typeName -eq 'SuperPutty.Scp.PscpBrowserPanel') {
            $grid = $form.Controls.Find('grid',$true)[0]
            "SCP grid back=$($grid.DefaultCellStyle.BackColor)"
        }
        $form.Dispose()
    }
    $login = New-Object SuperPutty.dlgLogin -ArgumentList 'test'
    $login.Dispose()
    $session = New-Object SuperPutty.Data.SessionData
    $editor = New-Object SuperPutty.dlgEditSession -ArgumentList $session,$icons
    $editor.Dispose()
    $main.Dispose()
    $icons.Dispose()
    if ($Theme -eq 'Dark Modern') {
        $toolbarPreview = New-Object Windows.Forms.Form
        $toolbarPreview.Text = 'Dark Modern toolbar borders'
        $toolbarPreview.Size = New-Object Drawing.Size -ArgumentList 760,140
        $toolbarPreview.TopMost = $true
        $toolbar = New-Object Windows.Forms.ToolStrip
        $inputs = @()
        foreach ($name in @('Protocol','Session','Commands','Host','Login')) {
            if ($name -in @('Host','Login')) {
                $item = New-Object Windows.Forms.ToolStripTextBox
            } else {
                $item = New-Object Windows.Forms.ToolStripComboBox
                if ($name -ne 'Commands') { $item.DropDownStyle = 'DropDownList' }
                $null = $item.Items.Add($name)
                $item.SelectedIndex = 0
            }
            $item.Text = $name
            $item.AutoSize = $false
            $item.Width = 130
            $null = $toolbar.Items.Add($item)
            $inputs += $item.Control
        }
        $toolbarPreview.Controls.Add($toolbar)
        [SuperPutty.Gui.ThemeManager]::Apply($toolbarPreview)
        $toolbarPreview.Show()
        [Windows.Forms.Application]::DoEvents()
        foreach ($inputControl in $inputs) {
            $null = $inputControl.Focus()
            $inputControl.GetType().GetMethod('RecreateHandle',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($inputControl,@()) | Out-Null
            $toolbarPreview.Refresh()
            [Windows.Forms.Application]::DoEvents()
            [Threading.Thread]::Sleep(500)
            $origin = $inputControl.Parent.PointToScreen($inputControl.Location)
            $sample = New-Object Drawing.Bitmap -ArgumentList 1,1
            $g = [Drawing.Graphics]::FromImage($sample)
            $g.CopyFromScreen($origin.X,($origin.Y + 5),0,0,$sample.Size)
            $g.Dispose()
            if ($sample.GetPixel(0,0).ToArgb() -ne [Drawing.Color]::FromArgb(45,45,48).ToArgb()) {
                throw "Toolbar border mismatch: $($inputControl.GetType().Name) $($sample.GetPixel(0,0))"
            }
            $sample.Dispose()
        }
        $bitmap = New-Object Drawing.Bitmap -ArgumentList $toolbarPreview.Width,$toolbarPreview.Height
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($toolbarPreview.Location,[Drawing.Point]::Empty,$toolbarPreview.Size)
        $graphics.Dispose()
        $backgroundPoint = $toolbar.PointToScreen((New-Object Drawing.Point -ArgumentList 700,5))
        $backgroundPoint.Offset(-$toolbarPreview.Left, -$toolbarPreview.Top)
        if ($bitmap.GetPixel($backgroundPoint.X,$backgroundPoint.Y).ToArgb() -ne [Drawing.Color]::FromArgb(24,24,24).ToArgb()) {
            throw 'Toolbar background does not match the Dark Modern menu surface'
        }
        $bitmap.Save((Join-Path $root 'results/toolbar-modern.png'))
        $bitmap.Dispose()
        $toolbarPreview.Dispose()
        'PASS matching toolbar borders after focus and handle recreation'
        $preview = New-Object Windows.Forms.Form
        $preview.Text = 'SuperPuTTY Dark Modern - docking renderer check'
        $preview.Size = New-Object Drawing.Size -ArgumentList 920,560
        $panel = New-Object WeifenLuo.WinFormsUI.Docking.DockPanel
        $panel.Dock = [Windows.Forms.DockStyle]::Fill
        $panel.DocumentStyle = [WeifenLuo.WinFormsUI.Docking.DocumentStyle]::DockingWindow
        [SuperPutty.Gui.ThemeManager]::ApplyToDockPanel($panel)
        $preview.Controls.Add($panel)
        [SuperPutty.Gui.ThemeManager]::Apply($preview)
        $docs = @()
        foreach ($title in @('Sessions','SSH - test tab','PowerShell - test tab')) {
            $doc = New-Object WeifenLuo.WinFormsUI.Docking.DockContent
            $doc.Text = $title
            [SuperPutty.Gui.ThemeManager]::Apply($doc)
            $label = New-Object Windows.Forms.Label
            $label.Text = if ($title -eq 'Sessions') { 'Saved sessions' } else { 'Docking preview - no remote connection' }
            $label.Dock = [Windows.Forms.DockStyle]::Fill
            $doc.Controls.Add($label)
            $state = if ($title -eq 'Sessions') { [WeifenLuo.WinFormsUI.Docking.DockState]::DockLeft } else { [WeifenLuo.WinFormsUI.Docking.DockState]::Document }
            $doc.Show($panel,$state)
            $docs += $doc
        }
        $preview.TopMost = $true
        $preview.Show()
        [Threading.Thread]::Sleep(500)
        [Windows.Forms.Application]::DoEvents()
        $docs[1].Activate()
        [Threading.Thread]::Sleep(300)
        [Windows.Forms.Application]::DoEvents()
        $bitmap = New-Object Drawing.Bitmap -ArgumentList $preview.Width,$preview.Height
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($preview.Location, [Drawing.Point]::Empty, $preview.Size)
        $graphics.Dispose()
        $bitmap.Save((Join-Path $root 'results/docking-modern.png'))
        $bitmap.Dispose()
        foreach ($doc in $docs) { $doc.Dispose() }
        $preview.Dispose()
        'PASS document tab rendering and activation'
    }
    'PASS'







} finally {
    $env:USERPROFILE = $previousProfile
    Set-Location $previousLocation
}
