using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Ustin.Work.LessMess.UmbarcoWrapper.Web.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly WrapperSession _session;

    public IndexModel(WrapperSession session) => _session = session;

    public async Task<IActionResult> OnGetAsync()
    {
        SessionRecord? record = await _session.GetAsync();
        return RedirectToPage(record is null ? "/Login" : "/Translations");
    }
}
