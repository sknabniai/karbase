﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow.ApprovalTask;

namespace Sungero.Docflow
{

  partial class ApprovalTaskSharedHandlers
  {
    public virtual void SignatoryChanged(Sungero.Docflow.Shared.ApprovalTaskSignatoryChangedEventArgs e)
    {
      if (Equals(e.NewValue, e.OldValue))
        return;
      
      var stages = Functions.ApprovalTask.Remote.GetStages(_obj).Stages;
      Functions.ApprovalTask.RefreshApprovalTaskForm(_obj, stages);
      Functions.ApprovalTask.Remote.UpdateReglamentApprovers(_obj, _obj.ApprovalRule, stages);
    }

    public virtual void StageNumberChanged(Sungero.Domain.Shared.IntegerPropertyChangedEventArgs e)
    {
      // Добавить в лог запись о предыдущем и новом номере этапа для упрощения анализа логов задачи.
      Logger.DebugFormat("Task:{0}. Stage number changed from {1} to {2}", _obj.Id, (e.OldValue ?? 0).ToString(), (e.NewValue ?? 0).ToString());
    }

    public virtual void ExchangeServiceChanged(Sungero.Docflow.Shared.ApprovalTaskExchangeServiceChangedEventArgs e)
    {
      if (Equals(e.NewValue, e.OldValue))
        return;
      
      // Обновить предметное отображение регламента.
      _obj.State.Controls.Control.Refresh();
    }

    public virtual void DeliveryMethodChanged(Sungero.Docflow.Shared.ApprovalTaskDeliveryMethodChangedEventArgs e)
    {
      if (Equals(e.NewValue, e.OldValue))
        return;
      
      if (e.NewValue == null || e.NewValue.Sid != Constants.MailDeliveryMethod.Exchange)
      {
        _obj.ExchangeService = null;
        _obj.State.Properties.ExchangeService.IsEnabled = false;
      }
      else
      {
        _obj.State.Properties.ExchangeService.IsEnabled = true;
        _obj.ExchangeService = Functions.ApprovalTask.Remote.GetExchangeServices(_obj).DefaultService;
      }
      
      Functions.ApprovalTask.RefreshApprovalTaskForm(_obj);
      // Обновить предметное отображение регламента.
      _obj.State.Controls.Control.Refresh();
      Functions.ApprovalTask.Remote.UpdateReglamentApprovers(_obj, _obj.ApprovalRule);
    }
    
    public virtual void AddresseeChanged(Sungero.Docflow.Shared.ApprovalTaskAddresseeChangedEventArgs e)
    {
      if (Equals(e.NewValue, e.OldValue))
        return;
      
      var stages = Functions.ApprovalTask.Remote.GetStages(_obj).Stages;
      Functions.ApprovalTask.RefreshApprovalTaskForm(_obj, stages);
      // Обновить обязательных согласующих.
      Sungero.Docflow.Functions.ApprovalTask.Remote.UpdateReglamentApprovers(_obj, _obj.ApprovalRule, stages);
    }
    
    public virtual void ReqApproversChanged(Sungero.Domain.Shared.CollectionPropertyChangedEventArgs e)
    {
      var shadowCopy = _obj.ReqApprovers.ToList();
      var distincted = shadowCopy.GroupBy(a => a.Approver).Select(a => a.First());
      foreach (var item in shadowCopy.Except(distincted))
      {
        _obj.ReqApprovers.Remove(item);
      }
    }
    
    public override void AuthorChanged(Sungero.Workflow.Shared.TaskAuthorChangedEventArgs e)
    {
      if (_obj.ApprovalRule != null)
      {
        _obj.ReqApprovers.Clear();
        
        var stages = Functions.ApprovalTask.Remote.GetStages(_obj).Stages;
        var managerStage = stages.Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Manager).FirstOrDefault();
        if (managerStage != null)
        {
          var manager = Docflow.PublicFunctions.ApprovalStage.Remote.GetRemoteStagePerformer(_obj, managerStage.Stage);
          if (manager != null && !manager.Equals(_obj.Author))
            _obj.ReqApprovers.AddNew().Approver = manager;
        }
        
        var reglamentApprovers = stages
          .Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Approvers)
          .OrderBy(num => num.Number)
          .SelectMany(p => p.Stage.Recipients)
          .Select(p => p.Recipient)
          .ToList();

        foreach (var approver in reglamentApprovers)
          _obj.ReqApprovers.AddNew().Approver = approver;
      }
    }
    
    public virtual void ApprovalRuleChanged(Sungero.Docflow.Shared.ApprovalTaskApprovalRuleChangedEventArgs e)
    {
      if (Equals(e.NewValue, e.OldValue))
        return;
      
      var stages = Functions.ApprovalTask.Remote.GetStages(_obj).Stages;
      // Очистить на клиенте, т.к. с сервера изменения могут прийти позже.
      _obj.Signatory = null;
      Functions.ApprovalTask.RefreshApprovalTaskForm(_obj, stages);
      Functions.ApprovalTask.Remote.ApprovalRuleChanged(_obj, e.NewValue, stages);
    }

    public virtual void DocumentGroupDeleted(Sungero.Workflow.Interfaces.AttachmentDeletedEventArgs e)
    {
      Functions.Module.SynchronizeAddendaAndAttachmentsGroup(_obj.AddendaGroup, null);
      
      // Очистить группу дополнительно.
      var document = OfficialDocuments.As(e.Attachment);
      if (OfficialDocuments.Is(document))
        Functions.OfficialDocument.RemoveRelatedDocumentsFromAttachmentGroup(OfficialDocuments.As(document), _obj.OtherGroup);

      _obj.Subject = Docflow.Resources.AutoformatTaskSubject;
    }

    public virtual void DocumentGroupAdded(Sungero.Workflow.Interfaces.AttachmentAddedEventArgs e)
    {
      var document = _obj.DocumentGroup.OfficialDocuments.First();

      using (TenantInfo.Culture.SwitchTo())
        _obj.Subject = Functions.Module.TrimSpecialSymbols(ApprovalTasks.Resources.TaskSubject, document.Name);
      
      if (!_obj.State.IsCopied)
      {
        Functions.Module.SynchronizeAddendaAndAttachmentsGroup(_obj.AddendaGroup, document);
        Functions.OfficialDocument.AddRelatedDocumentsToAttachmentGroup(document, _obj.OtherGroup);
      }
      
      _obj.ApprovalRule = Functions.OfficialDocument.Remote.GetApprovalRules(document).FirstOrDefault();
      _obj.DocumentExternalApprovalState = document.ExternalApprovalState ?? ApprovalTask.DocumentExternalApprovalState.Empty;
      
      Functions.OfficialDocument.DocumentAttachedInMainGroup(document, _obj);
      
      var stages = Functions.ApprovalTask.Remote.GetStages(_obj).Stages;
      Functions.ApprovalTask.RefreshApprovalTaskForm(_obj, stages);
      Functions.ApprovalTask.Remote.ApprovalRuleChanged(_obj, _obj.ApprovalRule, stages);
    }

    public override void SubjectChanged(Sungero.Domain.Shared.StringPropertyChangedEventArgs e)
    {
      // TODO: удалить код после исправления бага 17930 (сейчас этот баг в TFS недоступен, он про автоматическое обрезание темы).
      if (e.NewValue != null && e.NewValue.Length > ApprovalTasks.Info.Properties.Subject.Length)
        _obj.Subject = e.NewValue.Substring(0, ApprovalTasks.Info.Properties.Subject.Length);
      
      if (string.IsNullOrWhiteSpace(e.NewValue))
        _obj.Subject = Docflow.Resources.AutoformatTaskSubject;
    }

    public virtual void AddApproversChanged(Sungero.Domain.Shared.CollectionPropertyChangedEventArgs e)
    {
      _obj.State.Controls.Control.Refresh();
    }
  }
}