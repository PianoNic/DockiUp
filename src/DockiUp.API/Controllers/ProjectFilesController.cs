using System.Text;
using DockiUp.Application.Commands;
using DockiUp.Application.Deployments;
using DockiUp.Application.Dtos;
using DockiUp.Domain;
using DockiUp.Infrastructure.Services;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Toamaisutaa.Abstractions;

namespace DockiUp.API.Controllers
{
    /// <summary>Browse, edit, upload and download the files in a DockiUp project's folder, on whichever host
    /// it lives. Paths are relative to the project folder ('/'-separated); the host confines them to it.</summary>
    [ApiController]
    [Route("api/Project/{projectId:guid}/Files")]
    public class ProjectFilesController(IMediator mediator, ICurrentUser? currentUser = null) : ControllerBase
    {
        private string? Actor => currentUser?.IsAuthenticated == true ? currentUser.Name : null;

        [HttpGet(Name = "ListProjectFiles")]
        [ProducesResponseType(typeof(ProjectFilesDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProjectFilesDto>> ListProjectFiles(Guid projectId, [FromQuery] string? path)
            => Ok(await mediator.Send(new ListProjectFilesQuery(projectId, path), HttpContext.RequestAborted));

        /// <summary>A text file's content for the editor (UTF-8, up to 1 MB; binary files are refused).</summary>
        [HttpGet("Content", Name = "ReadProjectFile")]
        [ProducesResponseType(typeof(ProjectFileContentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProjectFileContentDto>> ReadProjectFile(Guid projectId, [FromQuery] string path)
            => Ok(await mediator.Send(new ReadProjectFileQuery(projectId, path), HttpContext.RequestAborted));

        /// <summary>Saves a text file. The compose file is validated first; a git-tracked file is committed and
        /// pushed. With <c>Deploy</c> a deployment is queued afterwards.</summary>
        [HttpPut("Content", Name = "SaveProjectFile")]
        [ProducesResponseType(typeof(SaveProjectFileResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SaveProjectFileResultDto>> SaveProjectFile(Guid projectId, [FromBody] SaveProjectFileRequest request)
        {
            if (Encoding.UTF8.GetByteCount(request.Content) > ProjectFileSystem.MaxTextBytes)
                throw new ArgumentException($"The file is too large to save here (limit {ProjectFileSystem.MaxTextBytes / 1024} KB).");
            var result = await mediator.Send(new SaveProjectFileCommand(projectId, request.Path, Encoding.UTF8.GetBytes(request.Content), Actor), HttpContext.RequestAborted);
            var deployment = request.Deploy
                ? await mediator.Send(new QueueDeploymentCommand(projectId, DeploymentTrigger.Manual, Actor), HttpContext.RequestAborted)
                : null;
            return Ok(new SaveProjectFileResultDto(result.Tracked, result.Commit, deployment));
        }

        /// <summary>Uploads one file into <paramref name="path"/> (a folder; the project folder when empty),
        /// replacing a file of the same name.</summary>
        [HttpPost("Upload", Name = "UploadProjectFile")]
        [RequestSizeLimit(ProjectFileSystem.MaxUploadBytes + 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = ProjectFileSystem.MaxUploadBytes + 1024 * 1024)]
        [ProducesResponseType(typeof(SaveProjectFileResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SaveProjectFileResultDto>> UploadProjectFile(Guid projectId, IFormFile file, [FromQuery] string? path)
        {
            if (file.Length > ProjectFileSystem.MaxUploadBytes)
                throw new ArgumentException($"'{file.FileName}' is too large (limit {ProjectFileSystem.MaxUploadBytes / 1024 / 1024} MB).");
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, HttpContext.RequestAborted);
            // Only the name counts: browsers may send a full client path.
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            var target = string.IsNullOrEmpty(path) ? name : $"{path.TrimEnd('/')}/{name}";
            var result = await mediator.Send(new SaveProjectFileCommand(projectId, target, buffer.ToArray(), Actor), HttpContext.RequestAborted);
            return Ok(new SaveProjectFileResultDto(result.Tracked, result.Commit, null));
        }

        [HttpGet("Download", Name = "DownloadProjectFile")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "application/octet-stream")]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> DownloadProjectFile(Guid projectId, [FromQuery] string path)
        {
            var bytes = await mediator.Send(new DownloadProjectFileQuery(projectId, path), HttpContext.RequestAborted);
            return File(bytes, "application/octet-stream", Path.GetFileName(path.Replace('\\', '/')));
        }

        /// <summary>Deletes a file or folder (recursively). Git-tracked files and the compose file are refused.</summary>
        [HttpDelete(Name = "DeleteProjectFile")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> DeleteProjectFile(Guid projectId, [FromQuery] string path)
        {
            await mediator.Send(new DeleteProjectFileCommand(projectId, path), HttpContext.RequestAborted);
            return NoContent();
        }

        [HttpPost("Folder", Name = "CreateProjectFolder")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> CreateProjectFolder(Guid projectId, [FromQuery] string path)
        {
            await mediator.Send(new CreateProjectFolderCommand(projectId, path), HttpContext.RequestAborted);
            return NoContent();
        }
    }

    public record SaveProjectFileRequest(string Path, string Content, bool Deploy);

    /// <summary><c>Tracked</c>: the file is versioned in git; <c>Commit</c>: the pushed commit (git projects);
    /// <c>Deployment</c>: the queued deployment when asked to deploy.</summary>
    public record SaveProjectFileResultDto(bool Tracked, string? Commit, DeploymentDto? Deployment);
}
