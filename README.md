# .NET Core WPF AWS TranscribeService Example

This repo is a small sample to show how to use the AWS SDK for .NET and the [Amazon Transcribe](https://docs.aws.amazon.com/transcribe/latest/dg/what-is-transcribe.html)
service. This was also an exercise for myself to try out building WPF apps in .NET Core using .NET Core preview 7.

The main AWS code to do the transcription is in the BeginTranscription_Click method from the MainWindow.xaml.cs file. The basic flow has the following steps.

* Upload the selected mp4 movie to S3
* Start the transcription job
* Poll for the completion of the job
* When job is complete download the transcription from the provided URL of the success job status.