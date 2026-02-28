import re

with open('src/DHSIntegrationAgent.App/UI/ViewModels/BatchesViewModel.cs', 'r') as f:
    content = f.read()

on_show_attachments_replacement = """    private void OnShowAttachments(BatchRow batch)
    {
        if (batch == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await using var uow = await _unitOfWorkFactory.CreateAsync(default);
                var localBatch = await uow.Batches.GetByBcrIdAsync(batch.BcrId.ToString(), default);

                if (localBatch != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var attachmentsVm = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AttachmentsViewModel>(
                            ((App)System.Windows.Application.Current).ServiceHost!.Services);

                        _ = attachmentsVm.InitializeAsync(localBatch.BatchId, $"Batch BCR ID: {batch.BcrId}");
                        _navigation.NavigateTo(attachmentsVm);
                    });
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Batch not found locally. Please process the batch first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading attachments: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        });
    }"""

# A bit tricky to replace because of brace matching, let's use a regex that matches the whole function
content = re.sub(
    r'private void OnShowAttachments\(BatchRow batch\).*?private async Task OnUploadAttachmentsAsync\(\)',
    on_show_attachments_replacement + '\n\n    private async Task OnUploadAttachmentsAsync()',
    content,
    flags=re.DOTALL
)

with open('src/DHSIntegrationAgent.App/UI/ViewModels/BatchesViewModel.cs', 'w') as f:
    f.write(content)
