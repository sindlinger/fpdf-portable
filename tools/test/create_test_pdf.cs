using System;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;

class CreateTestPDF
{
    static void Main()
    {
        string filename = "test_timestamps.pdf";
        Document document = new Document();
        
        try
        {
            PdfWriter writer = PdfWriter.GetInstance(document, new FileStream(filename, FileMode.Create));
            
            // Set document metadata with dates
            document.AddCreationDate();
            document.AddTitle("Test PDF for Timestamps");
            document.AddAuthor("FilterPDF Test");
            document.AddSubject("Testing timestamp features");
            
            document.Open();
            
            // Add some content
            document.Add(new Paragraph("Test PDF for Timestamp Features"));
            document.Add(new Paragraph("Created on: " + DateTime.Now.ToString()));
            document.Add(new Paragraph("This PDF is used to test the timestamp filtering options."));
            
            // Add more pages
            for (int i = 1; i <= 3; i++)
            {
                document.NewPage();
                document.Add(new Paragraph($"Page {i + 1}"));
                document.Add(new Paragraph("Content for testing timestamp filters."));
            }
            
            document.Close();
            Console.WriteLine($"Created {filename} successfully!");
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }
    }
}