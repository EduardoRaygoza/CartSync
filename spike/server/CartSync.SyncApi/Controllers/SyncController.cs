using CartSync.SyncApi.Domain;
using CartSync.SyncApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace CartSync.SyncApi.Controllers;

[ApiController]
[Route("api/v1/sync")]
public sealed class SyncController(CanonicalReducer reducer, InMemoryHouseholdStore store) : ControllerBase
{
    [HttpPost("operations")]
    public async Task<ActionResult<IReadOnlyList<OperationResult>>> Push(OperationBatch batch, CancellationToken cancellationToken)
    {
        var householdLock = store.GetLock(batch.HouseholdId);
        await householdLock.WaitAsync(cancellationToken);
        try
        {
            var state = store.Get(batch.HouseholdId);
            return Ok(batch.Operations.OrderBy(operation => operation.DeviceSequence)
                .Select(operation => reducer.Apply(state, batch.MemberId, operation)).ToArray());
        }
        finally { householdLock.Release(); }
    }

    [HttpGet("changes")]
    public ActionResult<ChangePage> Changes([FromQuery] Guid householdId, [FromQuery] long after = 0)
    {
        var state = store.Get(householdId);
        return Ok(new ChangePage(after, state.Cursor, state.Changes.Where(change => change.Cursor > after).Take(500).ToArray()));
    }

    [HttpGet("bootstrap")]
    public ActionResult<ChangePage> Bootstrap([FromQuery] Guid householdId) => Changes(householdId);

    [HttpGet("events")]
    public async Task Events([FromQuery] Guid householdId, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        var last = -1L;
        while (!cancellationToken.IsCancellationRequested)
        {
            var cursor = store.Get(householdId).Cursor;
            if (cursor != last)
            {
                await Response.WriteAsync($"event: checkpoint\ndata: {cursor}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                last = cursor;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }
}
