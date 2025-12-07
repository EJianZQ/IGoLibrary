# IGoLibrary Mac 版 - UI 界面说明

## 界面结构

### 主窗口 (MainWindow.axaml)

采用左右分栏布局：
- **左侧导航栏** (200px 宽)：包含 4 个导航按钮
  - 🔐 登录
  - 🎯 抢座
  - 📚 图书馆信息
  - ⚙️ 设置

- **右侧内容区**：根据 `CurrentPage` 属性动态显示不同页面

### 页面导航

使用 `MainViewModel.CurrentPage` 属性控制页面切换：
```csharp
[ObservableProperty]
private string _currentPage = "Login";

[RelayCommand]
private void NavigateTo(string page)
{
    CurrentPage = page;
}
```

XAML 中使用 `ObjectConverters.Equal` 转换器控制页面可见性：
```xml
<views:LoginView IsVisible="{Binding CurrentPage, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Login}"/>
<views:GrabSeatView IsVisible="{Binding CurrentPage, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=GrabSeat}"/>
```

## 抢座界面 (GrabSeatView.axaml)

### 布局结构

```
┌─────────────────────────────────────────────────────────────┐
│ 标题：抢座监控                                                │
│ ┌─────────┐ ┌──────────────┐ ┌──────────────┐              │
│ │刷新座位  │ │添加到抢座列表 │ │清空抢座列表   │              │
│ └─────────┘ └──────────────┘ └──────────────┘              │
│                                                              │
│ 抢座模式: [激进/随机/保守▼]  定时抢座: [00:00]              │
│ [监控开关] [停止监控]  状态: False                           │
│ ─────────────────────────────────────────────────────────── │
│ ┌──────────┬──────────┬────────────────────────────────┐   │
│ │所有座位   │待抢座位   │运行日志                         │   │
│ ├──────────┼──────────┼────────────────────────────────┤   │
│ │座位号│状态│ 1号      │[12:30:15] 获取座位信息成功      │   │
│ │  1  │无人│ 5号      │[12:30:16] 1号座位: 有人         │   │
│ │  2  │有人│ 10号     │[12:30:17] 5号座位: 无人         │   │
│ │  3  │无人│          │[12:30:18] 开始预定5号座位       │   │
│ │ ... │... │          │...                              │   │
│ └──────────┴──────────┴────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 关键 XAML 代码

#### 1. DataGrid 绑定座位列表

```xml
<DataGrid ItemsSource="{Binding SeatList}"
          SelectedItem="{Binding SelectedSeat}"
          AutoGenerateColumns="False"
          IsReadOnly="True"
          GridLinesVisibility="All"
          SelectionMode="Single">
    <DataGrid.Columns>
        <DataGridTextColumn Header="座位号"
                            Binding="{Binding Name}"
                            Width="*"/>
        <DataGridTextColumn Header="状态"
                            Binding="{Binding Status}"
                            Width="*"/>
        <DataGridTextColumn Header="座位Key"
                            Binding="{Binding Key}"
                            Width="2*"/>
    </DataGrid.Columns>
</DataGrid>
```

**绑定说明**：
- `ItemsSource="{Binding SeatList}"`: 绑定到 `ObservableCollection<SeatKeyData>`
- `SelectedItem="{Binding SelectedSeat}"`: 双向绑定选中的座位
- `AutoGenerateColumns="False"`: 手动定义列
- 每列绑定到 `SeatKeyData` 的属性：`Name`, `Status`, `Key`

#### 2. 待抢座位列表

```xml
<ListBox ItemsSource="{Binding WaitingGrabSeats}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal" Spacing="5">
                <TextBlock Text="{Binding Name}"/>
                <TextBlock Text="号"/>
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

**绑定说明**：
- 绑定到 `ObservableCollection<SeatKeyData>`
- 使用 `DataTemplate` 自定义显示格式
- 显示为 "1号"、"5号" 的格式

#### 3. 运行日志区域

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto">
    <ItemsControl ItemsSource="{Binding Logs}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <TextBlock Text="{Binding}"
                           TextWrapping="Wrap"
                           FontFamily="Consolas,Menlo,Monaco,monospace"
                           FontSize="12"
                           Margin="0,2"/>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</ScrollViewer>
```

**绑定说明**：
- 绑定到 `ObservableCollection<string>`
- 使用等宽字体显示日志
- 自动换行，支持滚动
- 日志从上到下显示（最新的在顶部，通过 `Logs.Insert(0, ...)` 实现）

#### 4. 监控控制

```xml
<!-- 抢座模式选择 -->
<ComboBox SelectedIndex="{Binding GrabMode}" Width="150">
    <ComboBoxItem Content="激进模式 (1秒)"/>
    <ComboBoxItem Content="随机模式 (4-8秒)"/>
    <ComboBoxItem Content="保守模式 (5秒)"/>
</ComboBox>

<!-- 定时抢座 -->
<TimePicker SelectedTime="{Binding TimingTime}" Width="120"/>

<!-- 监控开关 -->
<ToggleSwitch IsChecked="{Binding IsMonitoring}"
              OnContent="监控中"
              OffContent="已停止"
              Command="{Binding StartMonitorCommand}"
              IsEnabled="{Binding !IsMonitoring}"/>

<!-- 停止按钮 -->
<Button Content="停止监控"
        Command="{Binding StopMonitorCommand}"
        IsEnabled="{Binding IsMonitoring}"/>
```

**绑定说明**：
- `GrabMode`: 绑定到 int 属性 (0=激进, 1=随机, 2=保守)
- `TimingTime`: 绑定到 `TimeSpan?` 属性
- `IsMonitoring`: 绑定到 bool 属性，控制开关状态
- 命令绑定：`StartMonitorCommand`, `StopMonitorCommand`

## 登录界面 (LoginView.axaml)

### 布局结构

```
┌─────────────────────────────────────┐
│                                     │
│      我去图书馆 - Mac 版             │
│                                     │
│   ┌───────────────────────────┐    │
│   │ 扫描二维码获取 Cookie      │    │
│   │                           │    │
│   │      [二维码图片]          │    │
│   │                           │    │
│   │ 使用微信扫描上方二维码     │    │
│   └───────────────────────────┘    │
│                                     │
│   或手动输入 Cookie:                │
│   ┌───────────────────────────┐    │
│   │                           │    │
│   └───────────────────────────┘    │
│                                     │
│   [验证 Cookie]  [保存 Cookie]     │
│                                     │
└─────────────────────────────────────┘
```

### 二维码图片显示

```xml
<Image Source="avares://IGoLibrary.Mac/Assets/qrcode.png"
       Width="200"
       Height="200"
       Stretch="Uniform"/>
```

**资源路径说明**：
- 使用 `avares://` 协议访问嵌入资源
- 格式：`avares://程序集名称/路径`
- 图片位置：`IGoLibrary.Mac/Assets/qrcode.png`

## 资源配置

### 项目文件配置 (IGoLibrary.Mac.csproj)

```xml
<ItemGroup>
  <AvaloniaResource Include="Assets\**" />
</ItemGroup>
```

这会将 `Assets` 文件夹下的所有文件标记为 `AvaloniaResource`，可以在 XAML 中通过 `avares://` 访问。

### 资源文件列表

```
IGoLibrary.Mac/Assets/
├── qrcode.png      (从原项目复制)
└── Library.png     (从原项目复制)
```

## ViewModel 注册

在 `App.axaml.cs` 中注册所有 ViewModel：

```csharp
// 注册 ViewModels
services.AddTransient<MainViewModel>();
services.AddTransient<GrabSeatViewModel>();
```

## 数据绑定总结

### GrabSeatViewModel 属性绑定

| 属性 | 类型 | 用途 | 绑定控件 |
|------|------|------|----------|
| `SeatList` | `ObservableCollection<SeatKeyData>` | 所有座位 | DataGrid |
| `WaitingGrabSeats` | `ObservableCollection<SeatKeyData>` | 待抢座位 | ListBox |
| `Logs` | `ObservableCollection<string>` | 运行日志 | ItemsControl |
| `IsMonitoring` | `bool` | 监控状态 | ToggleSwitch, Button |
| `GrabMode` | `int` | 抢座模式 | ComboBox |
| `TimingTime` | `TimeSpan?` | 定时时间 | TimePicker |
| `SelectedSeat` | `SeatKeyData?` | 选中座位 | DataGrid |

### 命令绑定

| 命令 | 用途 | 绑定控件 |
|------|------|----------|
| `StartMonitorCommand` | 开始监控 | ToggleSwitch |
| `StopMonitorCommand` | 停止监控 | Button |
| `RefreshSeatsCommand` | 刷新座位 | Button |
| `AddToGrabListCommand` | 添加到抢座列表 | Button |
| `ClearGrabListCommand` | 清空抢座列表 | Button |

## 关键特性

### 1. 响应式 UI
- 使用 `ObservableCollection` 自动更新 UI
- 使用 `ObservableProperty` 自动实现 INotifyPropertyChanged
- 使用 `RelayCommand` 自动实现命令模式

### 2. 线程安全
- 日志更新使用 `Dispatcher.UIThread.Post()` 确保在 UI 线程执行
- 所有 UI 更新都通过数据绑定，避免直接操作控件

### 3. 用户体验
- 监控中禁用开始按钮，启用停止按钮
- 实时显示监控状态
- 日志自动滚动，最新日志在顶部
- 日志数量限制（500条），防止内存泄漏

## 下一步工作

1. 实现 LoginViewModel 和登录逻辑
2. 实现图书馆信息页面
3. 实现设置页面
4. 添加更多 UI 交互细节
5. 测试和优化

## 运行项目

```bash
cd IGoLibrary.Mac
dotnet restore
dotnet run
```

注意：由于 Mac 环境没有安装 .NET SDK，需要先安装 .NET 6 SDK 才能运行。
