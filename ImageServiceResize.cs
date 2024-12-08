using System.Text.RegularExpressions;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProcessImageService.Function
{
    public class ImageServiceResize
    {
        private readonly ILogger<ImageServiceResize> _logger;

        private static IImageEncoder? GetEncoder(string extension)
        {
            IImageEncoder? imageEncoder = null;

            extension = extension.Replace(".", "");
            
            var isSupported = Regex.IsMatch(extension, "gif|png|jpg|jpeg", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension)
                {
                    case "png":
                        imageEncoder = new PngEncoder();
                        break;
                    case "gif":
                        imageEncoder = new GifEncoder();
                        break;
                    case "jpg":
                        imageEncoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        imageEncoder = new JpegEncoder();
                        break;
                    default:
                        break;
                }
                
            }
            return imageEncoder;
        }
        

        public ImageServiceResize(ILogger<ImageServiceResize> logger)
        {
            _logger = logger;
        }

        [Function(nameof(ImageServiceResize))]
        public async Task Run(
            [ServiceBusTrigger("imagequeue", Connection = "hobbyhubbus_SERVICEBUS")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            _logger.LogInformation("Message ID: {id}", message.MessageId);
            _logger.LogInformation("Message Body: {body}", message.Body);
            _logger.LogInformation("Message Content-Type: {contentType}", message.ContentType);

            var resizeInfo = JsonConvert.DeserializeObject<ImageResizeDto>(message.Body.ToString());
            var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            
            var storageAccount = new BlobServiceClient(storageConnectionString);

            var containerClient = storageAccount.GetBlobContainerClient("images");
            
            await containerClient.CreateIfNotExistsAsync();

            var blobName = resizeInfo!.FileName;
            
            //get reference of the blob storage
            var cloudBlockBlob = containerClient.GetBlobClient(blobName);
            
            var ms = new MemoryStream();
            await cloudBlockBlob.DownloadToAsync(ms);
            
            byte[] bytes = ms.ToArray();
            
            var extension = Path.GetExtension(resizeInfo.FileName);
            var encoder = GetEncoder(extension);
            
            using(var outputStream = new MemoryStream())
            using (Image<Rgba32> image = Image.Load<Rgba32>(bytes))
            {
                _logger.LogInformation("Image Resize had started");
                
                image.Mutate(x => x.Resize(new ResizeOptions()
                {
                    Size = new Size(resizeInfo.Width, resizeInfo.Height),
                    Compand = true,
                    Mode = ResizeMode.Max,
                }));
                
                await image.SaveAsync(outputStream, encoder!);
                outputStream.Position = 0;
                
                //Create a new file and upload it to a blob storage
                var newFileName = $"resize_{resizeInfo.FileName}";
                
                var blobServiceClient = new BlobServiceClient(storageConnectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient("resize-images");
                await blobContainerClient.CreateIfNotExistsAsync();
                
                var blobCopy = blobContainerClient.GetBlobClient(newFileName);
                await blobCopy.UploadAsync(outputStream, overwrite: true);

                _logger.LogInformation($"Upload completed: {newFileName}");
            }

            // Complete the message
            await messageActions.CompleteMessageAsync(message);
            
            
        }
    }
}
