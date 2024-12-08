namespace ProcessImageService.Function;

public class ImageResizeDto
{
	public required string FileName { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public required string Url { get; set; }
	public required string ImageContainer { get; set; }
}