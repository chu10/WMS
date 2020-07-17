﻿using Coldairarrow.Business.IT;
using Coldairarrow.Business.PB;
using Coldairarrow.Entity.IT;
using Coldairarrow.Entity.TD;
using Coldairarrow.IBusiness.DTO;
using Coldairarrow.Util;
using EFCore.Sharding;
using LinqKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Reflection;
using System.Threading.Tasks;

namespace Coldairarrow.Business.TD
{
    public partial class TD_SendBusiness : BaseBusiness<TD_Send>, ITD_SendBusiness, ITransientDependency
    {
        public TD_SendBusiness(IDbAccessor db, IServiceProvider svcProvider)
            : base(db)
        {
            _ServiceProvider = svcProvider;
        }
        readonly IServiceProvider _ServiceProvider;

        public async Task<PageResult<TD_Send>> GetDataListAsync(TD_SendPageInput input)
        {
            var q = GetIQueryable()
                .Include(i => i.Customer)
                .Include(i => i.Address)
                .Include(i => i.CreateUser)
                .Include(i => i.AuditUser)
                .Where(w => w.StorId == input.StorId);
            var where = LinqHelper.True<TD_Send>();
            var search = input.Search;


            if (search.Status.HasValue)
                where = where.And(w => w.Status == search.Status.Value);
            if (!search.Code.IsNullOrEmpty())
                where = where.And(w => w.Code.Contains(search.Code) || w.RefCode.Contains(search.Code));
            if (!search.OutType.IsNullOrEmpty())
                where = where.And(w => w.Type == search.OutType);
            //if (search.OutStorTimeStart.HasValue)
            //    where = where.And(w => w.OutTime >= search.OutStorTimeStart.Value);
            //if (search.OutStorTimeEnd.HasValue)
            //    where = where.And(w => w.OutTime <= search.OutStorTimeEnd.Value);

            return await q.Where(where).GetPageResultAsync(input);
        }

        public async Task<TD_Send> GetTheDataAsync(string id)
        {
           var result = await this.GetIQueryable()
                .Include(i => i.SendDetails)
                    .ThenInclude(t => t.Location)
                .Include(i => i.SendDetails)
                    .ThenInclude(t => t.Material)
                .Include(i => i.SendDetails)
                    .ThenInclude(t => t.Measure)
                .SingleOrDefaultAsync(w => w.Id == id);

            return result;

            //return await this.GetIQueryable()
            //    .Include(i => i.SendDetails)
            //        .ThenInclude(t => t.Location)
            //    .Include(i => i.SendDetails)
            //        .ThenInclude(t => t.Material)
            //    .Include(i => i.SendDetails)
            //        .ThenInclude(t => t.Measure)
            //    .SingleOrDefaultAsync(w => w.Id == id);
        }

        [DataAddLog(UserLogType.发货管理, "Code", "发货单")]
        [Transactional]
        public async Task AddDataAsync(TD_Send data)
        {
            if (data.Code.IsNullOrEmpty())
            {
                var codeSvc = _ServiceProvider.GetRequiredService<IPB_BarCodeTypeBusiness>();
                data.Code = await codeSvc.Generate("TD_Send");
            }
            data.TotalNum = data.SendDetails.Sum(s => s.PlanNum);
            data.TotalAmt = data.SendDetails.Sum(s => s.Amount);
            await InsertAsync(data);
        }

        public async Task UpdateDetailAsync(TD_Send data)
        {
            data.SendNum = data.SendDetails.Sum(s => s.SendNum);
            data.TotalAmt = data.SendDetails.Sum(s => s.Amount);
            await UpdateAsync(data);
        }

        [DataEditLog(UserLogType.发货管理, "Code", "发货单")]
        public async Task UpdateDataAsync(TD_Send data)
        {
            var curDetail = data.SendDetails;
            var listDetail = await Db.GetIQueryable<TD_SendDetail>().Where(w => w.SendId == data.Id).ToListAsync();

            var curIds = curDetail.Select(s => s.Id).ToList();
            var dbIds = listDetail.Select(s => s.Id).ToList();
            var deleteIds = dbIds.Except(curIds).ToList();
            var detailSvc = _ServiceProvider.GetRequiredService<ITD_SendDetailBusiness>();
            if (deleteIds.Count > 0)
                await detailSvc.DeleteDataAsync(deleteIds);

            var addIds = curIds.Except(dbIds).ToList();
            if (addIds.Count > 0)
            {
                var listAdds = curDetail.Where(w => addIds.Contains(w.Id)).ToList();
                await detailSvc.AddDataAsync(listAdds);
            }

            var updateIds = curIds.Except(addIds).ToList();
            if (updateIds.Count > 0)
            {
                var listUpdates = curDetail.Where(w => updateIds.Contains(w.Id)).ToList();
                await detailSvc.UpdateDataAsync(listUpdates);
            }

            data.SendNum = data.SendDetails.Sum(s => s.SendNum);
            data.TotalAmt = data.SendDetails.Sum(s => s.Amount);

            await UpdateAsync(data);
        }


        public async Task Approval(AuditDTO audit)
        {
            var data = await this.GetEntityAsync(audit.Id);
            if (audit.AuditType == AuditType.Confirm)
            {
                data.Status = 1;
                data.ConfirmTime = audit.AuditTime;
                data.ConfirmUserId = audit.AuditUserId;
            }
            if (audit.AuditType == AuditType.Cancel)
            {
                data.Status = 2;
                data.ConfirmTime = audit.AuditTime;
                data.ConfirmUserId = audit.AuditUserId;
            }
            if (audit.AuditType == AuditType.Approve)//审核通过
            {            
                data.Status = 3;
                data.AuditeTime = audit.AuditTime;
                data.AuditUserId = audit.AuditUserId;

            }
            if (audit.AuditType == AuditType.Reject)
            {
                data.Status = 4;
                data.AuditeTime = audit.AuditTime;
                data.AuditUserId = audit.AuditUserId;
            }
            await UpdateAsync(data);
        }
    }
}