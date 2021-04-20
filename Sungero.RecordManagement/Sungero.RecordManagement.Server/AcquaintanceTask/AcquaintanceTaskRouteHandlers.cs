﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.RecordManagement.AcquaintanceTask;
using Sungero.Workflow;

namespace Sungero.RecordManagement.Server
{
  partial class AcquaintanceTaskRouteHandlers
  {
    public virtual void StartBlock3(Sungero.RecordManagement.Server.AcquaintanceAssignmentArguments e)
    {
      if (_obj.Deadline.HasValue)
        e.Block.AbsoluteDeadline = _obj.Deadline.Value;
      
      e.Block.IsParallel = true;
      e.Block.Subject = AcquaintanceTasks.Resources.AcquaintanceAssignmentSubjectFormat(_obj.DocumentGroup.OfficialDocuments.First().DisplayValue);
      var recipients = Functions.AcquaintanceTask.GetParticipants(_obj);
      foreach (var recipient in recipients)
        e.Block.Performers.Add(recipient);
      
      // Запомнить участников ознакомления.
      Functions.AcquaintanceTask.StoreAcquainters(_obj);
      
      // Синхронизировать приложения отправляемого документа.
      var document = _obj.DocumentGroup.OfficialDocuments.First();
      Docflow.PublicFunctions.Module.SynchronizeAddendaAndAttachmentsGroup(_obj.AddendaGroup, document);
      
      // Выдать права на просмотр наблюдателям.
      var documents = _obj.DocumentGroup.OfficialDocuments.Concat(_obj.AddendaGroup.OfficialDocuments).ToList();
      var observers = _obj.Observers.Select(x => x.Observer).ToList();
      Docflow.PublicFunctions.Module.GrantReadRightsForAttachments(documents, observers);
    }
    
    public virtual void StartAssignment3(Sungero.RecordManagement.IAcquaintanceAssignment assignment, Sungero.RecordManagement.Server.AcquaintanceAssignmentArguments e)
    {
      // Для ознакомления под подпись указать пояснение.
      if (_obj.IsElectronicAcquaintance == false)
        assignment.Description = AcquaintanceTasks.Resources.FromSignAssignmentDesription;
    }

    public virtual void CompleteAssignment3(Sungero.RecordManagement.IAcquaintanceAssignment assignment, Sungero.RecordManagement.Server.AcquaintanceAssignmentArguments e)
    {
      // Запомнить номер версии и хеш для отчета.
      var mainDocumentTaskVersionNumber = _obj.AcquaintanceVersions
        .Where(a => a.IsMainDocument == true)
        .Select(a => a.Number)
        .FirstOrDefault();
      
      var mainDocument = _obj.DocumentGroup.OfficialDocuments.First();
      Functions.AcquaintanceAssignment.StoreAcquaintanceVersion(assignment, mainDocument, true, mainDocumentTaskVersionNumber);
      
      var addenda = _obj.AddendaGroup.OfficialDocuments;
      foreach (var addendum in addenda)
        Functions.AcquaintanceAssignment.StoreAcquaintanceVersion(assignment, addendum, false, null);
    }
    
    public virtual void StartBlock4(Sungero.RecordManagement.Server.AcquaintanceFinishAssignmentArguments e)
    {
      e.Block.RelativeDeadlineDays = 2;
      e.Block.Performers.Add(_obj.Author);
      e.Block.Subject = AcquaintanceTasks.Resources.AcquaintanceFinishAssignmentSubjectFormat(_obj.DocumentGroup.OfficialDocuments.First().DisplayValue);
    }
    
    public virtual void StartAssignment4(Sungero.RecordManagement.IAcquaintanceFinishAssignment assignment, Sungero.RecordManagement.Server.AcquaintanceFinishAssignmentArguments e)
    {
      // Для ознакомления под подпись указать пояснение.
      if (_obj.IsElectronicAcquaintance == false)
        assignment.Description = AcquaintanceTasks.Resources.SelfSignAcquaintanceDecription;
      else
        assignment.Description = AcquaintanceTasks.Resources.ElectronicAcquaintanceDecription;
    }
  }
}