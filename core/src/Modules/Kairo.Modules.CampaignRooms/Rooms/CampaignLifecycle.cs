using Kairo.Contracts.Audit;
using Kairo.Contracts.CampaignRooms;

namespace Kairo.Modules.CampaignRooms.Rooms;

internal sealed class CampaignLifecycle(RoomWorkflow workflow, KairoDatabase database) : ICampaignLifecycle
{
    public async Task<CampaignAdvance> AdvanceAsync(Guid roomId, string signal, AuditActor actor, CancellationToken cancellationToken)
    {
        var owned = !database.HasTransaction;
        if (owned)
        {
            await database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var result = await workflow.AdvanceAsync(roomId, signal, actor, cancellationToken);
            if (owned)
            {
                if (result.Error == MoveError.None)
                {
                    await database.CommitAsync(cancellationToken);
                }
                else
                {
                    await database.RollbackAsync(cancellationToken);
                }
            }

            return result.Error switch
            {
                MoveError.None => CampaignAdvance.Moved,
                MoveError.NotFound => CampaignAdvance.NotFound,
                _ => CampaignAdvance.WrongState,
            };
        }
        catch
        {
            if (owned)
            {
                await database.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }
}
