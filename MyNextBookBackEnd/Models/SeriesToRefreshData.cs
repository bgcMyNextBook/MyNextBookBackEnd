using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;






namespace MyNextBookBackEnd.Models
{
    public class SeriesToRefreshData
    {
        [Required]
        public string Name { get; set; }
        [Required]
        public string seriesId { get; set; }
        [Required]
        public List<BookRefreshData> booksInSeries { get; set; }
        public List<BookRefreshData> booksMissingFromSeries { get; set; }


     
    }
    public class BookRefreshData
    {
        [Required]
        public string BookTitle { get; set; }
        public string Author { get; set; }
        public string ISBN_10 { get; set; }
        public string ISBN_13 { get; set; }
        public string sysMynbIDAsString { get; set; }
    }
}
