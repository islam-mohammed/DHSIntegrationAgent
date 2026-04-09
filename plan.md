Plan:
1. Update `IBatchTracker` interface to include `void RemoveTrackedBatch(long batchId);`
2. Implement `RemoveTrackedBatch` in `BatchTracker.cs` to remove the matching `BatchProgressViewModel` from `ActiveBatches` on the UI thread.
3. Update `BatchesViewModel.cs`:
   - Add `public RelayCommand<BatchProgressViewModel> RemoveActiveBatchCommand { get; }`
   - Initialize it: `RemoveActiveBatchCommand = new RelayCommand<BatchProgressViewModel>(OnRemoveActiveBatch);`
   - Implement `private void OnRemoveActiveBatch(BatchProgressViewModel progressItem) { _batchTracker.RemoveTrackedBatch(progressItem.InternalBatchId); }`
4. Update `BatchesView.xaml`:
   - Add a button in the Active Batches item template to remove the item.
   - Use `materialDesign:PackIcon Kind="Close"`.
   - Bind the button's Command to the parent `BatchesViewModel.RemoveActiveBatchCommand`.
   - Use a `Style` with `DataTrigger` on `IsCompleted` or `ProgressPercentage` == 100 to only show the button when complete. (The user asked: "It should appear only if the progress bar show 100%"). I'll use `ProgressPercentage == 100`. Wait, `ProgressPercentage` is double. `IsCompleted` is a boolean, which gets set to true when `Percentage >= 100` or on error. I will use `ProgressPercentage == 100` or `IsCompleted`. Let's use `IsCompleted` because it's a bool, but wait, the progress bar might show 100% and it completes.
5. Complete pre commit checks and submit.

Let's refine step 4:
```xml
<Grid Margin="0,0,0,8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <TextBlock Text="Batch Number" FontSize="11" Foreground="{StaticResource BrushMuted}"/>
    <TextBlock Grid.Column="1" Text="{Binding BatchNumber}" FontWeight="SemiBold" FontSize="13" Margin="0,0,8,0"/>
    <Button Grid.Column="2"
            Command="{Binding DataContext.RemoveActiveBatchCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
            CommandParameter="{Binding}"
            Background="Transparent"
            BorderBrush="Transparent"
            Foreground="{StaticResource BrushMuted}"
            Padding="0" Height="20" Width="20"
            ToolTip="Remove completed task">
        <Button.Style>
            <Style TargetType="Button">
                <Setter Property="Visibility" Value="Collapsed"/>
                <Style.Triggers>
                    <DataTrigger Binding="{Binding IsCompleted}" Value="True">
                        <Setter Property="Visibility" Value="Visible"/>
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Button.Style>
        <materialDesign:PackIcon Kind="Close" Width="16" Height="16"/>
    </Button>
</Grid>
```
Wait, the user explicitly said "It should appear only if the progress bar show 100%". `IsCompleted` is True when `Percentage >= 100`. And when error happens. `ProgressPercentage` is a double. If `IsCompleted` is used, it covers 100% and errors. Is it okay? Yes, if it completes with error, they probably want to remove it too.
Let's use `<DataTrigger Binding="{Binding ProgressPercentage}" Value="100">` to be extremely literal to the user request.
Wait, `ProgressPercentage` is double (`100.0`). WPF might not match `Value="100"` with `double` strictly, or it might. It's safer to use `<DataTrigger Binding="{Binding ProgressPercentage}" Value="100.0">` or just `IsCompleted`. Since `IsCompleted` is set when percentage >= 100, `IsCompleted` is basically that. I'll use `IsCompleted` as it's cleaner. Wait, I can also add `IsCompleted` check AND a `ProgressPercentage >= 100` if needed, but `IsCompleted` does what the user intends (completed task).

Let me check `BatchesView.xaml` namespace for materialDesign. Yes, it's used.
