using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Amazon;

using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;

using Amazon.S3;
using Amazon.S3.Transfer;

using Amazon.TranscribeService;
using Amazon.TranscribeService.Model;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WpfCoreAWSTranscribeSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }


        private async void BeginTranscription_Click(object sender, RoutedEventArgs e)
        {
            this._ctlBeginTranscription.IsEnabled = false;
            try
            {
                this._ctlStatusLog.Text = "";
                this._ctlTranscription.Text = "";

                var filepath = this._ctlFilepath.Text;
                if(string.IsNullOrEmpty(filepath))
                {
                    MessageBox.Show($"An mp4 file must be selected first before transcribing", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if(!System.IO.File.Exists(filepath))
                {
                    MessageBox.Show($"File {filepath} does not exist", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var s3Key = System.IO.Path.GetFileName(filepath);
                var s3Bucket = _ctlS3Bucket.Text;


                var chain = new CredentialProfileStoreChain();
                AWSCredentials credentials;
                if (!chain.TryGetAWSCredentials(this._ctlProfile.Text, out credentials))
                {
                    MessageBox.Show($"Profile {this._ctlProfile.Text} was not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var region = RegionEndpoint.GetBySystemName(this._ctlRegion.Text);

                var transcriptionJobName = $"{s3Key}-{Guid.NewGuid().ToString()}";

                using (var s3Client = new AmazonS3Client(credentials, region))
                using (var transcribeClient = new AmazonTranscribeServiceClient(credentials, region))
                using (var httpClient = new HttpClient()) // Http Client to download the transcription once complete
                {
                    AppendStatusLine("Ensuring S3 bucket exists");
                    await s3Client.PutBucketAsync(s3Bucket);


                    var transferUtility = new TransferUtility(s3Client);

                    AppendStatusLine("Starting upload");

                    var uploadRequest = new TransferUtilityUploadRequest
                    {
                        FilePath = filepath,
                        BucketName = s3Bucket,
                        Key = s3Key
                    };

                    uploadRequest.UploadProgressEvent += ProgressUploadStatus;

                    await transferUtility.UploadAsync(uploadRequest);

                    var mediaFileUri = $"https://s3.{region.SystemName}.amazonaws.com/{s3Bucket}/{s3Key}";
                    AppendStatusLine($"Upload Complete to: {mediaFileUri}");

                    await transcribeClient.StartTranscriptionJobAsync(new StartTranscriptionJobRequest
                    {
                        LanguageCode = LanguageCode.EnUS,
                        Media = new Media
                        {
                            MediaFileUri = mediaFileUri
                        },
                        MediaFormat = MediaFormat.Mp4,
                        TranscriptionJobName = transcriptionJobName
                    });
                    AppendStatusLine($"Started transcription job: {transcriptionJobName}");

                    GetTranscriptionJobRequest request = new GetTranscriptionJobRequest { TranscriptionJobName = transcriptionJobName };
                    GetTranscriptionJobResponse response = null;
                    do
                    {
                        AppendStatusLine($"... {DateTime.Now} Waiting for transcription job to complete");
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        response = await transcribeClient.GetTranscriptionJobAsync(request);
                    } while (response.TranscriptionJob.TranscriptionJobStatus == TranscriptionJobStatus.IN_PROGRESS);

                    if(response.TranscriptionJob.TranscriptionJobStatus == TranscriptionJobStatus.FAILED)
                    {
                        AppendStatusLine($"Transcription job failed: {response.TranscriptionJob.FailureReason}");
                        return;
                    }

                    AppendStatusLine("Job Done");

                    var transcriptionDocument = await httpClient.GetStringAsync(response.TranscriptionJob.Transcript.TranscriptFileUri);
                    var root = JsonConvert.DeserializeObject(transcriptionDocument) as JObject;

                    var sb = new StringBuilder();
                    foreach(JObject transcriptionNode in root["results"]["transcripts"])
                    {
                        if(sb.Length != 0)
                        {
                            sb.AppendLine("\n\n");
                        }

                        sb.Append(transcriptionNode["transcript"]);
                    }

                    this._ctlTranscription.Text = sb.ToString();
                }
            }
            catch(Exception ex)
            {
                AppendStatusLine($"Unknown error: {ex.Message}");
            }
            finally
            {
                this._ctlBeginTranscription.IsEnabled = true;
            }
        }

        int _lastReportedStatus = 0;
        private void ProgressUploadStatus(object sender, UploadProgressArgs e)
        {
            if(e.PercentDone != this._lastReportedStatus)
            {
                this._lastReportedStatus = e.PercentDone;
                AppendStatusLine($" ... Percent Upload: {e.PercentDone} %");
            }
        }

        private void AppendStatusLine(string line)
        {
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                this._ctlStatusLog.Text += line + Environment.NewLine;
                this._ctlStatusLog.ScrollToEnd();
            }));
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

            // Set filter for file extension and default file extension 
            dlg.DefaultExt = ".mp4";
            dlg.Filter = "Movie Files (*.mp4)|*.mp4";

            if(dlg.ShowDialog().GetValueOrDefault())
            {
                this._ctlFilepath.Text = dlg.FileName;
            }
        }
    }
}
