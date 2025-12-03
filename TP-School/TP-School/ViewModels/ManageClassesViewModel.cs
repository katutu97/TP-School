using Microsoft.AspNetCore.Mvc.Rendering;
using TP_School.Models;

namespace TP_School.ViewModels
{
    public class ManageClassesViewModel
    {
        public List<SchoolClass> Classes { get; set; }
        public List<SelectListItem> AvailableTeachers { get; set; }
    }
}
