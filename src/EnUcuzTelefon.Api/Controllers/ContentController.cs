using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/content")]
public class ContentController : BaseApiController
{
    private readonly IApplicationDbContext _context;

    public ContentController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("articles")]
    public async Task<IActionResult> GetArticles(CancellationToken cancellationToken)
    {
        var articles = await _context.Articles
            .OrderByDescending(a => a.PublishedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dtos = articles.Select(a => new ArticleDto(
            a.Id.ToString(),
            a.Slug,
            a.Title,
            a.Excerpt,
            a.Content,
            a.CoverImageUrl,
            a.Category,
            a.Tags,
            a.PublishedAt.ToString("yyyy-MM-dd"),
            a.UpdatedAt?.ToString("yyyy-MM-dd"),
            a.ReadingTimeMinutes,
            a.Author)).ToList();

        return Ok(dtos);
    }

    [HttpGet("articles/{slug}")]
    public async Task<IActionResult> GetArticleBySlug(string slug, CancellationToken cancellationToken)
    {
        var a = await _context.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (a == null) return NotFound();

        return Ok(new ArticleDto(
            a.Id.ToString(),
            a.Slug,
            a.Title,
            a.Excerpt,
            a.Content,
            a.CoverImageUrl,
            a.Category,
            a.Tags,
            a.PublishedAt.ToString("yyyy-MM-dd"),
            a.UpdatedAt?.ToString("yyyy-MM-dd"),
            a.ReadingTimeMinutes,
            a.Author));
    }

    [HttpGet("qa")]
    public async Task<IActionResult> GetQAItems(CancellationToken cancellationToken)
    {
        var items = await _context.QAItems
            .OrderByDescending(q => q.HelpfulCount)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dtos = items.Select(q => new QAItemDto(
            q.Id.ToString(),
            q.Slug,
            q.Question,
            q.Answer,
            q.Category,
            q.Tags,
            q.HelpfulCount,
            q.PublishedAt.ToString("yyyy-MM-dd"))).ToList();

        return Ok(dtos);
    }
}
