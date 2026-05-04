# 侧栏收缩/展开的示例代码：

```axml

			<!-- 左侧：会场列表 -->
			<Border DockPanel.Dock="Left" Width="{Binding SidebarListWidth}" Margin="0,0,8,0" CornerRadius="6"
					ClipToBounds="True"
					Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}">
				<Border.Transitions>
					<Transitions>
						<DoubleTransition Property="Width" Duration="0:0:0.25" Easing="CubicEaseInOut" />
					</Transitions>
				</Border.Transitions>
				<DockPanel Margin="8">
					<TextBlock DockPanel.Dock="Top" Text="会场列表"
							   FontSize="14" FontWeight="SemiBold" Margin="3,0,0,8" />
					<Border DockPanel.Dock="Bottom">
						<Panel>
							<!-- 展开状态 -->
							<StackPanel Margin="10,0,10,4" Spacing="8" Opacity="{Binding IsSidebarExpanded, Converter={x:Static conv:BoolConverters.TrueToVisible}}"
										IsHitTestVisible="{Binding IsSidebarExpanded}" HorizontalAlignment="Left">
								<StackPanel.Transitions>
									<Transitions>
										<DoubleTransition Property="Opacity" Duration="0:0:0.18" Easing="CubicEaseInOut" />
									</Transitions>
								</StackPanel.Transitions>

								<Button Command="{Binding LoadVenueListCommand}" CommandParameter="DataManagement"
										HorizontalContentAlignment="Left" HorizontalAlignment="Left">
									<StackPanel Orientation="Horizontal">
										<fic:FluentIcon Icon="{x:Static ficEnum:Icon.ArrowSyncCircle}" FontSize="20" Height="20"/>
										<TextBlock Text="刷新会场" FontSize="13" VerticalAlignment="Center"/>
									</StackPanel>
								</Button>
								<Button Command="{Binding NewVenueCommand}" CommandParameter="VenueConfiguration"
										HorizontalContentAlignment="Left" HorizontalAlignment="Left">
									<StackPanel Orientation="Horizontal">
										<fic:FluentIcon Icon="{x:Static ficEnum:Icon.Add}" FontSize="20" Height="20"/>
										<TextBlock Text="新建会场" FontSize="13" VerticalAlignment="Center"/>
									</StackPanel>
								</Button>
								<Button Command="{Binding ToggleSidebarCommand}"
										HorizontalContentAlignment="Left" HorizontalAlignment="Left">
									<StackPanel Orientation="Horizontal">
										<fic:FluentIcon Icon="{x:Static ficEnum:Icon.PanelLeftContract}" FontSize="20" Height="20"/>
										<TextBlock Text="收起侧栏" FontSize="13" VerticalAlignment="Center"/>
									</StackPanel>
								</Button>
							</StackPanel>

							<!-- 折叠状态 -->
							<StackPanel Margin="10,0,10,4" Spacing="8" Opacity="{Binding IsSidebarExpanded, Converter={x:Static conv:BoolConverters.FalseToVisible}}"
										IsHitTestVisible="{Binding IsSidebarCollapsed}" HorizontalAlignment="Center" VerticalAlignment="Bottom">
								<StackPanel.Transitions>
									<Transitions>
										<DoubleTransition Property="Opacity" Duration="0:0:0.2" Easing="CubicEaseInOut" />
									</Transitions>
								</StackPanel.Transitions>
								<Button HorizontalContentAlignment="Left"
										Command="{Binding LoadVenueListCommand}"
										ToolTip.Tip="刷新会场">
									<fic:FluentIcon Icon="{x:Static ficEnum:Icon.ArrowSync}" FontSize="20" Height="20"/>
								</Button>
								<Button HorizontalContentAlignment="Left"
										Command="{Binding NewVenueCommand}"
										ToolTip.Tip="新建会场">
									<fic:FluentIcon Icon="{x:Static ficEnum:Icon.Add}" FontSize="20" Height="20"/>
								</Button>
								<Button HorizontalContentAlignment="Left"
										Command="{Binding ToggleSidebarCommand}"
										ToolTip.Tip="展开列表">
									<fic:FluentIcon Icon="{x:Static ficEnum:Icon.PanelLeftExpand}" FontSize="20" Height="20"/>
								</Button>
							</StackPanel>
						</Panel>
					</Border>

```

```cs

// ── 侧边栏折叠 ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarCollapsed))]
    private bool _isSidebarExpanded = true; //默认侧栏展开状态

    public bool IsSidebarCollapsed => !IsSidebarExpanded;

    [ObservableProperty]
    private double _sidebarListWidth = 240; //侧栏宽度（展开状态）

    private bool _userWantsSidebarExpanded = true;

    public void OnWindowWidthChanged (double windowWidth)
    {
        if (windowWidth < 750) //触发宽度
            IsSidebarExpanded = false;
        else
            IsSidebarExpanded = _userWantsSidebarExpanded;
    }

    partial void OnIsSidebarExpandedChanged (bool value)
        => SidebarListWidth = value ? 240 : 80; //侧栏宽度（展开/折叠）

    [RelayCommand]
    private void ToggleSidebar ()
    {
        _userWantsSidebarExpanded = !_userWantsSidebarExpanded;
        IsSidebarExpanded = _userWantsSidebarExpanded;
    }

```