﻿using System;
using System.Collections.Generic;
using System.Linq;
using CommonLibrary;
using Sungero.Company;
using Sungero.Content;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow;
using Sungero.Docflow.ApprovalStage;
using Sungero.Docflow.ApprovalTask;
using Sungero.Docflow.OfficialDocument;
using Sungero.Domain.Shared;
using Sungero.Workflow;
using ExchDocumentType = Sungero.Exchange.ExchangeDocumentInfoServiceDocuments.DocumentType;
using ReviewResults = Sungero.Docflow.ApprovalReviewAssignment.Result;

namespace Sungero.Docflow.Server
{
  partial class ApprovalTaskFunctions
  {
    #region Контрол "Состояние"
    
    /// <summary>
    /// Получить список заданий по задаче.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Список заданий.</returns>
    [Remote]
    public static List<IAssignment> GetTaskAssigments(ITask task)
    {
      return Assignments.GetAll(x => Equals(x.Task, task)).ToList();
    }
    
    /// <summary>
    /// Построить модель контрола состояния документа.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Модель контрола состояния.</returns>
    public Sungero.Core.StateView GetStateView(Sungero.Docflow.IOfficialDocument document)
    {
      if (_obj.DocumentGroup.OfficialDocuments.Any(d => Equals(document, d)) ||
          _obj.AddendaGroup.OfficialDocuments.Any(d => Equals(document, d)))
        return this.GetStateView();
      else
        return StateView.Create();
    }
    
    /// <summary>
    /// Построить модель контрола состояния задачи на согласование по регламенту.
    /// </summary>
    /// <returns>Модель контрола состояния задачи на согласование по регламенту.</returns>
    [Remote(IsPure = true)]
    public Sungero.Core.StateView GetStateView()
    {
      // Добавить заголовок отправки для стартованной задачи.
      var stateView = StateView.Create();
      
      var iterations = Functions.Module.GetIterationDates(_obj);
      
      var comment = Functions.Module.GetTaskUserComment(_obj, ApprovalTasks.Resources.ApprovalText);
      
      var startedByUser = Sungero.CoreEntities.Users.As(Sungero.Workflow.WorkflowHistories.GetAll()
                                                        .Where(h => h.EntityId == _obj.Id)
                                                        .Where(h => h.Operation == Sungero.Workflow.WorkflowHistory.Operation.Start)
                                                        .OrderBy(h => h.HistoryDate)
                                                        .Select(h => h.User)
                                                        .FirstOrDefault());
      
      if (_obj.Started.HasValue)
        Docflow.PublicFunctions.OfficialDocument
          .AddUserActionBlock(stateView, _obj.Author, ApprovalTasks.Resources.StateViewDocumentSentForApproval, _obj.Started.Value, _obj, comment, startedByUser);
      else
        Docflow.PublicFunctions.OfficialDocument
          .AddUserActionBlock(stateView, _obj.Author, ApprovalTasks.Resources.StateViewTaskDrawCreated, _obj.Created.Value, _obj, comment, _obj.Author);
      
      // Добавить основной блок для задачи.
      var taskBlock = this.AddTaskBlock(stateView);
      
      // Получить все задания по задаче.
      var taskAssignments = Assignments.GetAll(a => Equals(a.Task, _obj)).OrderBy(a => a.Created).ToList();
      
      foreach (var iteration in iterations)
      {
        var date = iteration.Date;
        var hasReworkBefore = iteration.IsRework;
        var hasRestartBefore = iteration.IsRestart;
        
        var nextIteration = iterations.Where(d => d.Date > date).FirstOrDefault();
        var nextDate = Calendar.Now;
        var hasRestartAfter = false;
        
        var isLastRound = nextIteration == null;
        if (!isLastRound)
        {
          nextDate = nextIteration.Date;
          hasRestartAfter = nextIteration.IsRestart;
        }
        
        // Получить задания в рамках круга согласования.
        var iterationAssignments = taskAssignments
          .Where(a => a.Created >= date && a.Created < nextDate)
          .OrderBy(a => a.Created)
          .ToList();
        
        if (!iterationAssignments.Any())
          continue;
        
        var firstAssignment = iterationAssignments.First();
        if (hasReworkBefore)
        {
          var reworkComment = Functions.Module.GetAssignmentUserComment(firstAssignment);
          Docflow.PublicFunctions.OfficialDocument
            .AddUserActionBlock(taskBlock, firstAssignment.Performer, ApprovalTasks.Resources.StateViewDocumentSentForReApproval,
                                firstAssignment.Completed.Value, _obj, reworkComment, firstAssignment.CompletedBy);
        }
        
        if (hasRestartBefore)
        {
          var restartComment = Functions.Module.GetTaskUserComment(_obj, firstAssignment.Created.Value, ApprovalTasks.Resources.ApprovalText);
          
          var restartedByUser = Sungero.CoreEntities.Users.As(Sungero.Workflow.WorkflowHistories.GetAll()
                                                              .Where(h => h.EntityId == firstAssignment.Task.Id)
                                                              .Where(h => h.HistoryDate.Between(date, nextDate))
                                                              .Where(h => h.Operation == Sungero.Workflow.WorkflowHistory.Operation.Restart)
                                                              .Select(h => h.User)
                                                              .FirstOrDefault());
          
          Docflow.PublicFunctions.OfficialDocument
            .AddUserActionBlock(taskBlock, firstAssignment.Author, ApprovalTasks.Resources.StateViewDocumentSentAfterRestart,
                                _obj.Started.Value, _obj, restartComment, restartedByUser);
        }
        
        if (!isLastRound)
        {
          // Добавить блок группировки для круга согласования.
          var roundBlock = taskBlock.AddChildBlock();
          roundBlock.AddLabel(ApprovalTasks.Resources.StateViewApprovalRound, Functions.Module.CreateStyle(true, true));
          roundBlock.AssignIcon(ApprovalTasks.Resources.OldApprove, StateBlockIconSize.Large);
          
          var roundStatus = hasRestartAfter ? ApprovalTasks.Resources.StateViewAborted : ApprovalTasks.Resources.StateViewNotApproved;
          Functions.Module.AddInfoToRightContent(roundBlock, roundStatus);
          
          this.AddAssignmentsBlocks(roundBlock, taskAssignments, iterationAssignments, StateBlockIconSize.Small);
        }
        else
          this.AddAssignmentsBlocks(taskBlock, taskAssignments, iterationAssignments, StateBlockIconSize.Large);
      }
      
      #region Спецобработка для задания на контроль возврата с отсрочкой создания
      
      if (_obj.Status == Workflow.Task.Status.InProcess)
      {
        var currentStage = _obj.ApprovalRule.Stages.FirstOrDefault(s => s.Number == _obj.StageNumber);
        if (currentStage != null && currentStage.Stage.StageType == Docflow.ApprovalStage.StageType.CheckReturn)
        {
          var delay = currentStage.Stage.StartDelayDays;
          if (delay.HasValue && delay > 0)
          {
            var activeReturnAssignments = ApprovalCheckReturnAssignments.GetAll(a => Equals(a.Task, _obj) &&
                                                                                a.Status == Workflow.AssignmentBase.Status.InProcess);
            if (!activeReturnAssignments.Any())
            {
              var block = taskBlock.AddChildBlock();
              block.AssignIcon(ApprovalTasks.Resources.WaitControl, StateBlockIconSize.Large);
              block.AddLabel(ApprovalTasks.Resources.StateViewWaitForCheckReturn, Docflow.PublicFunctions.Module.CreateHeaderStyle());
              block.AddLineBreak();
              block.AddLabel(string.Format("{0}: {1}",
                                           ApprovalTasks.Resources.StateViewAssignmentCreationTerms,
                                           Functions.Module.ToShortDateShortTime(_obj.ControlReturnStartDate.Value.ToUserTime())),
                             Docflow.PublicFunctions.Module.CreatePerformerDeadlineStyle());
              
              Functions.Module.AddInfoToRightContent(block, ApprovalTasks.Resources.StateViewInProcess);
            }
          }
        }
      }
      
      #endregion

      return stateView;
    }
    
    /// <summary>
    /// Добавить блок задачи согласования.
    /// </summary>
    /// <param name="stateView">Схема представления.</param>
    /// <returns>Добавленный блок.</returns>
    private StateBlock AddTaskBlock(StateView stateView)
    {
      var taskBlock = stateView.AddBlock();
      
      var isDraft = _obj.Status == Workflow.Task.Status.Draft;
      var headerStyle = Docflow.PublicFunctions.Module.CreateHeaderStyle(isDraft);
      var labelStyle = Docflow.PublicFunctions.Module.CreateStyle(false, isDraft, false);
      
      taskBlock.Entity = _obj;
      taskBlock.AssignIcon(OfficialDocuments.Info.Actions.SendForApproval, StateBlockIconSize.Large);
      taskBlock.IsExpanded = _obj.Status == Workflow.Task.Status.InProcess;
      taskBlock.AddLabel(ApprovalTasks.Resources.Approval, headerStyle);
      taskBlock.AddLineBreak();
      taskBlock.AddLabel(ApprovalTasks.Resources.StateViewApprovalRule, labelStyle);
      taskBlock.AddHyperlink(_obj.ApprovalRule.Name, Hyperlinks.Get(_obj.ApprovalRule));
      if ((_obj.Status == Workflow.Task.Status.InProcess || _obj.Status == Workflow.Task.Status.Draft) && _obj.MaxDeadline.HasValue)
      {
        var deadline = Functions.Module.ToShortDateShortTime(_obj.MaxDeadline.Value.ToUserTime());
        taskBlock.AddLabel(string.Format(" {0}: {1}", ApprovalTasks.Resources.StateViewExpectedDeadline, deadline), labelStyle);
      }
      
      var status = string.Empty;
      if (_obj.Status == Workflow.Task.Status.InProcess)
        status = ApprovalTasks.Resources.StateViewInProcess;
      else if (_obj.Status == Workflow.Task.Status.Completed)
        status = ApprovalTasks.Resources.StateViewCompleted;
      else if (_obj.Status == Workflow.Task.Status.Aborted)
        status = ApprovalTasks.Resources.StateViewAborted;
      else if (_obj.Status == Workflow.Task.Status.Suspended)
        status = ApprovalTasks.Resources.StateViewSuspended;
      else if (_obj.Status == Workflow.Task.Status.Draft)
        status = ApprovalTasks.Resources.StateViewDraft;
      
      Functions.Module.AddInfoToRightContent(taskBlock, status, labelStyle);
      
      return taskBlock;
    }
    
    private void AddAssignmentsBlocks(StateBlock block,
                                      List<IAssignment> taskAssignments,
                                      List<IAssignment> roundAssignments,
                                      StateBlockIconSize iconSize)
    {
      // Блок группировки согласования.
      var approvalAssignmentList = new List<IAssignment>();
      roundAssignments = roundAssignments.OrderBy(a => a.Id).ToList();
      foreach (var assignment in roundAssignments)
      {
        // Признак прекращенного конкурентного задания по контролю возврата. Только при условии, что есть хоть одно выполненное задание.
        var isCompletedAbortedControl = ApprovalCheckReturnAssignments.Is(assignment) &&
          (roundAssignments.Any(a => ApprovalCheckReturnAssignments.Is(a) && a.Status == Workflow.AssignmentBase.Status.Completed) &&
           assignment.Status == Workflow.AssignmentBase.Status.Aborted);
        
        // Для согласований добавить группировочный блок.
        var isApprovalBlock = ApprovalAssignments.Is(assignment) || ApprovalManagerAssignments.Is(assignment);
        if (isApprovalBlock)
          approvalAssignmentList.Add(assignment);
        else if (!isCompletedAbortedControl)
          this.AddAssignmentBlock(block, assignment, false, false, iconSize);
        
        // Следующее задание.
        var nextAssignments = taskAssignments.Where(a => a.Created >= assignment.Created && a.Id > assignment.Id);
        var nextAssignment = nextAssignments.OrderBy(a => a.Created).FirstOrDefault();
        
        // Завершить формирование группы согласования.
        var nextAssignmentIsNotApproval = nextAssignment == null || !roundAssignments.Contains(nextAssignment) ||
          (!ApprovalAssignments.Is(nextAssignment) && !ApprovalManagerAssignments.Is(nextAssignment));
        if (nextAssignmentIsNotApproval && isApprovalBlock)
        {
          this.AddApprovalStageBlocks(block, approvalAssignmentList, nextAssignment, iconSize);
          approvalAssignmentList.Clear();
        }
      }
    }
    
    private void AddApprovalStageBlocks(StateBlock block, List<IAssignment> assignments, IAssignment nextAssignment, StateBlockIconSize iconSize)
    {
      if (!assignments.Any())
        return;
      
      // Добавить блок группировки этапа согласования.
      var approvalStageBlock = block.AddChildBlock();
      approvalStageBlock.NeedGroupChildren = true;
      approvalStageBlock.AddLabel(ApprovalTasks.Resources.StateViewApprovalStage, Docflow.PublicFunctions.Module.CreateHeaderStyle());
      approvalStageBlock.AddLineBreak();
      approvalStageBlock.IsExpanded = assignments.Any(a => a.Status == Workflow.AssignmentBase.Status.InProcess);
      
      // Добавить информацию по исполнителям группы согласования.
      var performersLabel = string.Join(", ", assignments.Select(a => Company.PublicFunctions.Employee.GetShortName(Employees.As(a.Performer), false)));
      approvalStageBlock.AddLabel(performersLabel, Docflow.PublicFunctions.Module.CreatePerformerDeadlineStyle());
      
      // Установить иконку для группы и добавить статус.
      var hasAbort = assignments.Any(a => a.Status == Workflow.AssignmentBase.Status.Aborted || a.Status == Workflow.AssignmentBase.Status.Suspended);
      var isApproved = !assignments.Any(a => a.Result != Docflow.ApprovalAssignment.Result.Approved);
      var hasRework = assignments.Any(a => a.Result == Docflow.ApprovalAssignment.Result.ForRevision);
      
      var lastAssignment = assignments.OrderByDescending(a => a.Created).First();
      var taskAbortHistories = WorkflowHistories.GetAll()
        .Where(h => h.EntityId == _obj.Id &&
               h.Operation == Sungero.Workflow.WorkflowHistory.Operation.Abort &&
               h.HistoryDate >= lastAssignment.Created);
      var hasTaskAbort = nextAssignment == null ?
        taskAbortHistories.Any() :
        taskAbortHistories.Any(h => h.HistoryDate <= nextAssignment.Created);
      
      var status = this.SetGroupIconAndGetGroupStatus(approvalStageBlock, isApproved, hasRework, nextAssignment, hasAbort, hasTaskAbort, iconSize);
      Functions.Module.AddInfoToRightContent(approvalStageBlock, status);
      
      // Добавить задания этапа.
      var orderedAssignments = assignments.OrderByDescending(a => a.Result.HasValue).ThenBy(a => a.Completed);
      foreach (var assignment in orderedAssignments)
      {
        this.AddAssignmentBlock(approvalStageBlock, assignment, true, false, iconSize);
      }
    }
    
    /// <summary>
    /// Добавить блок по заданию.
    /// </summary>
    /// <param name="parentBlock">Ведущий блок.</param>
    /// <param name="assignment">Задание.</param>
    /// <param name="isApprovalBlock">Признак: согласование или нет.</param>
    /// <param name="isResolutionBlock">Признак: блок резолюции или нет.</param>
    /// <param name="iconSize">Размер иконки.</param>
    /// <returns>Блок с заданием.</returns>
    private StateBlock AddAssignmentBlock(StateBlock parentBlock, IAssignment assignment, bool isApprovalBlock, bool isResolutionBlock, StateBlockIconSize iconSize)
    {
      // Добавить отдельный блок резолюции для внесения результата рассмотрения или схлопнутых заданий.
      var needAddResolution = false;
      if (!isResolutionBlock && ApprovalReviewAssignments.Is(assignment) &&
          (assignment.Result == Docflow.ApprovalReviewAssignment.Result.AddResolution ||
           assignment.Result == Docflow.ApprovalReviewAssignment.Result.Informed ||
           assignment.Result == Docflow.ApprovalReviewAssignment.Result.AddActionItem ||
           assignment.Result == Docflow.ApprovalReviewAssignment.Result.Abort))
      {
        var reviewAssignment = ApprovalReviewAssignments.As(assignment);
        if (reviewAssignment.CollapsedStagesTypesRe.Count > 1)
        {
          needAddResolution = true;
        }
        else
        {
          // Для не схлопнутого задания рассмотрения заменить его на резолюцию.
          isResolutionBlock = true;
          needAddResolution = false;
        }
      }
      
      // Стили.
      var headerStyle = Docflow.PublicFunctions.Module.CreateHeaderStyle();
      var performerDeadlineStyle = Docflow.PublicFunctions.Module.CreatePerformerDeadlineStyle();
      
      // Исполнитель, срок, статус.
      var performerAndDeadlineAndStatus = this.GetPerformerAndDeadlineAndStatus(assignment, isResolutionBlock);
      var performer = performerAndDeadlineAndStatus.PerformerShortName;
      var deadline = performerAndDeadlineAndStatus.Deadline;
      var status = performerAndDeadlineAndStatus.Status;
      if (string.IsNullOrWhiteSpace(performer))
        return null;

      // Добавить блок.
      var block = parentBlock.AddChildBlock();
      block.Entity = assignment;
      
      // Установить иконку.
      this.SetIcon(block, assignment, isApprovalBlock, isResolutionBlock, iconSize);

      // Заполнить основное содержимое.
      if (isApprovalBlock)
      {
        block.AddLabel(performer, performerDeadlineStyle);
        block.AddLabel(deadline, performerDeadlineStyle);
      }
      else
      {
        // Заполнить заголовок.
        var header = this.GetHeader(assignment, isResolutionBlock);
        block.AddLabel(header, headerStyle);

        // Заполнить "Кому".
        var performerLabel = assignment.Status != Workflow.AssignmentBase.Status.Completed ?
          string.Format("{0}: {1}{2}", OfficialDocuments.Resources.StateViewTo, performer, deadline) :
          string.Format("{0}{1}", performer, deadline);
        
        // Для резолюции указать адресата вместо исполнителя.
        if (isResolutionBlock)
        {
          var task = ApprovalTasks.As(assignment.Task);
          var addresseeShortName = Company.PublicFunctions.Employee.GetShortName(task.Addressee, false);
          performerLabel = string.Format("{0}: {1} {2}", ApprovalTasks.Resources.StateViewAuthor, addresseeShortName, deadline);
        }
        
        block.AddLineBreak();
        block.AddLabel(performerLabel, performerDeadlineStyle);
        
        if (ApprovalSendingAssignments.Is(assignment) ||
            GetCollapsedStagesTypes(assignment).Contains(Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.Sending))
        {
          var task = ApprovalTasks.As(assignment.Task);
          if (task != null)
          {
            var isManyAddresseesOutgoingDocument = OutgoingDocumentBases.Is(task.DocumentGroup.OfficialDocuments.FirstOrDefault()) &&
              OutgoingDocumentBases.As(task.DocumentGroup.OfficialDocuments.FirstOrDefault()).IsManyAddressees == true;
            
            if (isManyAddresseesOutgoingDocument || task.DeliveryMethod != null)
            {
              var service = string.Empty;
              var method = string.Empty;
              
              if (isManyAddresseesOutgoingDocument)
                method = ApprovalRuleBases.Resources.StateViewSendToManyAddressees;
              else if (task.DeliveryMethod != null)
              {
                service = (task.DeliveryMethod.Sid == Constants.MailDeliveryMethod.Exchange && task.ExchangeService != null) ?
                  task.ExchangeService.Name :
                  string.Empty;
                method = task.DeliveryMethod.Name;
              }
              block.AddLineBreak();
              var note = ApprovalRuleBases.Resources.StateViewSendNoteFormat(method, service);
              block.AddLabel(note, Docflow.Functions.Module.CreateNoteStyle());
            }
          }
        }
      }

      // Заполнить примечание.
      this.AddComment(block, assignment, isResolutionBlock);
      
      // Заполнить статус.
      if (!string.IsNullOrWhiteSpace(status))
        Functions.Module.AddInfoToRightContent(block, status);
      
      // Заполнить просрочку.
      if (assignment.Status == Workflow.AssignmentBase.Status.InProcess)
        Functions.OfficialDocument.AddDeadlineHeaderToRight(block, assignment.Deadline.Value, assignment.Performer);
      
      // Добавить блок резолюции.
      if (needAddResolution)
        this.AddAssignmentBlock(parentBlock, assignment, false, true, iconSize);
      
      return block;
    }
    
    private static List<Enumeration?> GetCollapsedStagesTypes(IAssignment assignment)
    {
      // Для каждого задания берем свою дочернюю коллекцию, т.к. теперь они везде имеют разные названия.
      var stagesTypes = new List<Enumeration?>();
      
      if (ApprovalPrintingAssignments.Is(assignment))
        stagesTypes = ApprovalPrintingAssignments.As(assignment).CollapsedStagesTypesPr.Select(c => c.StageType).ToList();
      
      if (ApprovalRegistrationAssignments.Is(assignment))
        stagesTypes = ApprovalRegistrationAssignments.As(assignment).CollapsedStagesTypesReg.Select(c => c.StageType).ToList();
      
      if (ApprovalSendingAssignments.Is(assignment))
        stagesTypes = ApprovalSendingAssignments.As(assignment).CollapsedStagesTypesSen.Select(c => c.StageType).ToList();
      
      if (ApprovalSigningAssignments.Is(assignment))
        stagesTypes = ApprovalSigningAssignments.As(assignment).CollapsedStagesTypesSig.Select(c => c.StageType).ToList();
      
      if (ApprovalReviewAssignments.Is(assignment))
        stagesTypes = ApprovalReviewAssignments.As(assignment).CollapsedStagesTypesRe.Select(c => c.StageType).ToList();
      
      if (ApprovalExecutionAssignments.Is(assignment))
        stagesTypes = ApprovalExecutionAssignments.As(assignment).CollapsedStagesTypesExe.Select(c => c.StageType).ToList();
      
      return stagesTypes;
    }
    
    /// <summary>
    /// Получить заголовок.
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <param name="isResolutionBlock">Признак: блок резолюции или нет.</param>
    /// <returns>Заголовок.</returns>
    public string GetHeader(IAssignment assignment, bool isResolutionBlock)
    {
      if (!isResolutionBlock &&
          (ApprovalPrintingAssignments.Is(assignment) || ApprovalRegistrationAssignments.Is(assignment) ||
           ApprovalSendingAssignments.Is(assignment) || ApprovalSigningAssignments.Is(assignment) ||
           ApprovalReviewAssignments.Is(assignment) || ApprovalExecutionAssignments.Is(assignment)))
      {
        var stagesTypes = GetCollapsedStagesTypes(assignment);
        
        if (stagesTypes.Count > 1)
        {
          var stages = new List<string>();
          
          // Используем foreach, так как Linq не работает с такой конструкцией.
          var header = string.Empty;
          foreach (var stage in stagesTypes)
          {
            var stageHeader = ApprovalReviewAssignments.Info.Properties.CollapsedStagesTypesRe.Properties.StageType
              .GetLocalizedValue(stage).ToLower();
            stages.Add(stageHeader);
          }
          header = string.Join(", ", stages);
          return Functions.Module.ReplaceFirstSymbolToUpperCase(header);
        }
      }
      
      var actionLabel = string.Empty;
      
      // Согласование.
      if (ApprovalAssignments.Is(assignment) || ApprovalManagerAssignments.Is(assignment))
        actionLabel = ApprovalTasks.Resources.StateViewApprovalProcess;

      // Подписание.
      if (ApprovalSigningAssignments.Is(assignment))
      {
        // Для подтверждения подписания указать это.
        var signAssignment = ApprovalSigningAssignments.As(assignment);
        if (signAssignment.IsConfirmSigning == true)
          actionLabel = ApprovalTasks.Resources.StateViewApprovedConfirmation;
        else
          actionLabel = ApprovalTasks.Resources.StateViewSigning;
      }

      // Регистрация.
      if (ApprovalRegistrationAssignments.Is(assignment))
        actionLabel = ApprovalTasks.Resources.StateViewRegistration;

      // Контроль возврата от контрагента.
      if (ApprovalCheckReturnAssignments.Is(assignment))
        actionLabel = ApprovalTasks.Resources.StateViewCheckReturn;
      
      // Доработка после согласования или после задания с доработкой.
      if (ApprovalReworkAssignments.Is(assignment))
        actionLabel = Functions.ApprovalTask.IsSignatoryAbortTask(assignment.Task, assignment.Created) || Functions.ApprovalTask.IsAddresseeAbortTask(assignment.Task, assignment.Created) ?
          ApprovalTasks.Resources.StateViewAbortApprovalAssignment :
          Functions.ApprovalTask.IsExternalSignatoryAbortTask(assignment.Task, assignment.Created) ?
          ApprovalTasks.Resources.StateViewDocumentReworkAfterExternalAbort : ApprovalTasks.Resources.StateViewDocumentRework;
      
      // Печать документа.
      if (ApprovalPrintingAssignments.Is(assignment))
        actionLabel = ApprovalTasks.Resources.StateViewPrintDocument;
      
      // Рассмотрение адресатом.
      if (ApprovalReviewAssignments.Is(assignment))
      {
        if (isResolutionBlock)
          actionLabel = ApprovalTasks.Resources.StateViewResolution;
        else
        {
          // Для внесения результата рассмотрения указать это.
          var reviewAssignment = ApprovalReviewAssignments.As(assignment);
          if (reviewAssignment.IsResultSubmission == true)
            actionLabel = ApprovalTasks.Resources.StateViewResultSubmission;
          else
            actionLabel = ApprovalTasks.Resources.StateViewReview;
        }
      }
      
      // Создание поручений.
      if (ApprovalExecutionAssignments.Is(assignment))
        actionLabel = ApprovalTasks.Resources.StateViewExecution;

      // Отправка контрагенту.
      if (ApprovalSendingAssignments.Is(assignment))
        actionLabel = ApprovalTasks.Resources.StateViewSendToCounterParty;
      
      // Задание.
      if (ApprovalSimpleAssignments.Is(assignment) || ApprovalCheckingAssignments.Is(assignment))
      {
        var assignmentSubject = ApprovalSimpleAssignments.Is(assignment) ?
          ApprovalSimpleAssignments.As(assignment).StageSubject :
          ApprovalCheckingAssignments.As(assignment).StageSubject;
        
        if (string.IsNullOrWhiteSpace(assignmentSubject))
          actionLabel = OfficialDocuments.Resources.StateViewAssignment;
        else
          return string.Format("{0}. {1}", OfficialDocuments.Resources.StateViewAssignment, assignmentSubject);
      }
      
      return actionLabel;
    }
    
    /// <summary>
    /// Получить исполнителя, срок и статус.
    /// </summary>
    /// <param name="assignment">Задание.</param>
    /// <param name="isResolutionBlock">Признак: блок резолюции или нет.</param>
    /// <returns>Структура короткое имя исполнителя, срок, статус.</returns>
    public Structures.ApprovalTask.StateViewAssignmentInfo GetPerformerAndDeadlineAndStatus(IAssignment assignment, bool isResolutionBlock)
    {
      var performerName = PublicFunctions.OfficialDocument.GetAuthor(assignment.Performer, assignment.CompletedBy);
      var actionLabel = string.Empty;
      var emptyResult = Structures.ApprovalTask.StateViewAssignmentInfo.Create(string.Empty, string.Empty, string.Empty);

      #region Завершенные задания
      
      if (assignment.Status == Workflow.AssignmentBase.Status.Completed)
      {
        var completed = Functions.Module.ToShortDateShortTime(assignment.Completed.Value.ToUserTime());
        
        // Согласование.
        if (ApprovalAssignments.Is(assignment) || ApprovalManagerAssignments.Is(assignment))
        {
          if (assignment.Result == Docflow.ApprovalAssignment.Result.Approved)
            actionLabel = ApprovalTasks.Resources.StateViewEndorsed;
          else if (assignment.Result == Docflow.ApprovalAssignment.Result.ForRevision)
            actionLabel = ApprovalTasks.Resources.StateViewNotApproved;
          else if (assignment.Result == Docflow.ApprovalAssignment.Result.Forward)
            actionLabel = ApprovalTasks.Resources.StateViewForwarded;
          else
            return emptyResult;
        }
        
        // Подписание.
        if (ApprovalSigningAssignments.Is(assignment))
        {
          if (assignment.Result == Docflow.ApprovalSigningAssignment.Result.Sign)
            actionLabel = ApprovalTasks.Resources.StateViewApproved;
          else if (assignment.Result == Docflow.ApprovalSigningAssignment.Result.ConfirmSign)
            actionLabel = ApprovalTasks.Resources.StateViewDone;
          else if (assignment.Result == Docflow.ApprovalSigningAssignment.Result.ForRevision)
            actionLabel = ApprovalTasks.Resources.StateViewNotApproved;
          else if (assignment.Result == Docflow.ApprovalSigningAssignment.Result.Abort)
            actionLabel = ApprovalTasks.Resources.SigningRefused;
          else
            return emptyResult;
        }
        
        // Регистрация.
        if (ApprovalRegistrationAssignments.Is(assignment))
        {
          actionLabel = ApprovalTasks.Resources.StateViewDone;
        }
        
        // Прекращение на доработке.
        if (ApprovalReworkAssignments.Is(assignment) && assignment.Status == Sungero.Workflow.AssignmentBase.Status.Aborted)
        {
          actionLabel = ApprovalTasks.Resources.StateViewAborted;
        }
        
        // Переадресация на доработке.
        if (ApprovalReworkAssignments.Is(assignment) && assignment.Result == Docflow.ApprovalReworkAssignment.Result.Forward)
        {
          actionLabel = Sungero.Docflow.ApprovalTasks.Resources.StateViewReworkForwarded;
        }
        
        // Контроль возврата.
        if (ApprovalCheckReturnAssignments.Is(assignment))
        {
          if (assignment.Result == Docflow.ApprovalCheckReturnAssignment.Result.Signed)
            actionLabel = ApprovalTasks.Resources.StateViewSignedByCounterparty;
          else if (assignment.Result == Docflow.ApprovalCheckReturnAssignment.Result.NotSigned)
            actionLabel = ApprovalTasks.Resources.StateViewNotSignedByCounterparty;
          else
            return emptyResult;
        }
        
        // Печать документа.
        if (ApprovalPrintingAssignments.Is(assignment))
          actionLabel = ApprovalTasks.Resources.StateViewDone;
        
        // Рассмотрение адресатом.
        if (ApprovalReviewAssignments.Is(assignment))
        {
          // Для резолюции вернуть пустой статус.
          if (isResolutionBlock)
            return Structures.ApprovalTask.StateViewAssignmentInfo.Create(string.Format("{0} ", performerName),
                                                                          string.Format("{0}: {1}", OfficialDocuments.Resources.StateViewDate, completed),
                                                                          string.Empty);
          
          // Для внесения результата рассмотрения указать это.
          if (assignment.Result == Docflow.ApprovalReviewAssignment.Result.ForRework)
            actionLabel = ApprovalTasks.Resources.StateViewNotApproved;
          else
            actionLabel = ApprovalTasks.Resources.StateViewDone;
        }
        
        // Создание поручений.
        if (ApprovalExecutionAssignments.Is(assignment))
          actionLabel = ApprovalTasks.Resources.StateViewDone;

        // Отправка контрагенту.
        if (ApprovalSendingAssignments.Is(assignment))
          actionLabel = ApprovalTasks.Resources.StateViewDone;
        
        // Задание.
        if (ApprovalSimpleAssignments.Is(assignment) || ApprovalCheckingAssignments.Is(assignment))
          actionLabel = ApprovalTasks.Resources.StateViewDone;

        if (!string.IsNullOrWhiteSpace(actionLabel))
          return Structures.ApprovalTask.StateViewAssignmentInfo.Create(string.Format("{0} ", performerName),
                                                                        string.Format("{0}: {1}", OfficialDocuments.Resources.StateViewDate, completed),
                                                                        actionLabel);
      }
      
      #endregion
      
      #region Задания в работе
      
      if (assignment.Status == Workflow.AssignmentBase.Status.InProcess ||
          assignment.Status == Workflow.AssignmentBase.Status.Aborted ||
          assignment.Status == Workflow.AssignmentBase.Status.Suspended)
      {
        var asgStatus = ApprovalTasks.Resources.StateViewAborted;
        if (assignment.Status == Workflow.AssignmentBase.Status.InProcess)
          asgStatus = assignment.IsRead == true ? ApprovalTasks.Resources.StateViewInProcess : ApprovalTasks.Resources.StateViewUnRead;
        var deadline = Functions.Module.ToShortDateShortTime(assignment.Deadline.Value.ToUserTime());
        return Structures.ApprovalTask.StateViewAssignmentInfo.Create(string.Format("{0} ", performerName),
                                                                      string.Format("{0}: {1}", OfficialDocuments.Resources.StateViewDeadline, deadline),
                                                                      asgStatus);
      }
      
      #endregion
      
      return emptyResult;
    }
    
    /// <summary>
    /// Установить иконку.
    /// </summary>
    /// <param name="block">Блок.</param>
    /// <param name="assignment">Задание.</param>
    /// <param name="isApprovalBlock">Признак блока согласования.</param>
    /// <param name="isResolutionBlock">Признак: блок резолюции или нет.</param>
    /// <param name="iconMainSize">Размер иконки.</param>
    private void SetIcon(StateBlock block, IAssignment assignment, bool isApprovalBlock, bool isResolutionBlock, StateBlockIconSize iconMainSize)
    {
      var iconSize = isApprovalBlock ? StateBlockIconSize.Small : iconMainSize;
      
      // Иконка по умолчанию.
      block.AssignIcon(StateBlockIconType.OfEntity, iconSize);

      // Прекращено, остановлено по ошибке.
      if (assignment.Status == Workflow.AssignmentBase.Status.Aborted ||
          assignment.Status == Workflow.AssignmentBase.Status.Suspended)
      {
        block.AssignIcon(StateBlockIconType.Abort, iconSize);
        return;
      }
      
      if (assignment.Result == null)
        return;
      
      // Согласовано.
      if (assignment.Result == Docflow.ApprovalAssignment.Result.Approved)
      {
        block.AssignIcon(ApprovalTasks.Resources.Approve, iconSize);
      }
      
      // Переадресовано.
      if (assignment.Result == Docflow.ApprovalAssignment.Result.Forward)
      {
        block.AssignIcon(FreeApprovalTasks.Resources.Forward, iconSize);
      }
      
      // На доработку.
      if (assignment.Result == Docflow.ApprovalCheckingAssignment.Result.ForRework)
      {
        block.AssignIcon(ApprovalTasks.Resources.Rework, iconSize);
      }
      
      // Не согласовано.
      if (assignment.Result == Docflow.ApprovalAssignment.Result.ForRevision ||
          assignment.Result == Docflow.ApprovalReviewAssignment.Result.ForRework)
      {
        block.AssignIcon(ApprovalTasks.Resources.Notapprove, iconSize);
      }
      
      // Задание выполнено.
      if (assignment.Result == Docflow.ApprovalSimpleAssignment.Result.Complete ||
          assignment.Result == Docflow.ApprovalCheckingAssignment.Result.Accept)
      {
        block.AssignIcon(ApprovalTasks.Resources.Completed, iconSize);
      }
      
      // Подписано, подписано контрагентом.
      if (assignment.Result == Docflow.ApprovalSigningAssignment.Result.Sign ||
          assignment.Result == Docflow.ApprovalCheckReturnAssignment.Result.Signed ||
          assignment.Result == Docflow.ApprovalSigningAssignment.Result.ConfirmSign)
      {
        block.AssignIcon(ApprovalTasks.Resources.Sign, iconSize);
      }
      
      // На повторное согласование.
      if (assignment.Result == Docflow.ApprovalReworkAssignment.Result.ForReapproving)
      {
        block.AssignIcon(StateBlockIconType.User, iconSize);
      }
      
      // Зарегистрировано.
      if (ApprovalRegistrationAssignments.Is(assignment) &&
          assignment.Result == Docflow.ApprovalRegistrationAssignment.Result.Execute)
      {
        block.AssignIcon(OfficialDocuments.Info.Actions.Register, iconSize);
      }
      
      // Распечатано.
      if (ApprovalPrintingAssignments.Is(assignment) &&
          assignment.Result == Docflow.ApprovalPrintingAssignment.Result.Execute)
      {
        block.AssignIcon(ApprovalTasks.Resources.Print, iconSize);
      }
      
      // Рассмотрено.
      if (ApprovalReviewAssignments.Is(assignment) &&
          (assignment.Result == Docflow.ApprovalReviewAssignment.Result.Informed ||
           assignment.Result == Docflow.ApprovalReviewAssignment.Result.AddActionItem ||
           assignment.Result == Docflow.ApprovalReviewAssignment.Result.AddResolution ||
           assignment.Result == Docflow.ApprovalReviewAssignment.Result.Abort))
      {
        if (isResolutionBlock)
        {
          block.AssignIcon(ApprovalReviewAssignments.Info.Actions.AddResolution, iconSize);
          if (assignment.Result == Docflow.ApprovalReviewAssignment.Result.Abort)
            block.AssignIcon(StateBlockIconType.Abort, iconSize);
        }
        else
        {
          // Для каждого задания берем свою дочернюю коллекцию, т.к. теперь они везде имеют разные названия.
          var stagesTypes = new List<Enumeration?> { };
          
          if (ApprovalPrintingAssignments.Is(assignment))
            stagesTypes = ApprovalPrintingAssignments.As(assignment).CollapsedStagesTypesPr.Select(c => c.StageType).ToList();
          
          if (ApprovalRegistrationAssignments.Is(assignment))
            stagesTypes = ApprovalRegistrationAssignments.As(assignment).CollapsedStagesTypesReg.Select(c => c.StageType).ToList();
          
          if (ApprovalSendingAssignments.Is(assignment))
            stagesTypes = ApprovalSendingAssignments.As(assignment).CollapsedStagesTypesSen.Select(c => c.StageType).ToList();
          
          if (ApprovalSigningAssignments.Is(assignment))
            stagesTypes = ApprovalSigningAssignments.As(assignment).CollapsedStagesTypesSig.Select(c => c.StageType).ToList();
          
          if (ApprovalReviewAssignments.Is(assignment))
            stagesTypes = ApprovalReviewAssignments.As(assignment).CollapsedStagesTypesRe.Select(c => c.StageType).ToList();
          
          if (ApprovalExecutionAssignments.Is(assignment))
            stagesTypes = ApprovalExecutionAssignments.As(assignment).CollapsedStagesTypesExe.Select(c => c.StageType).ToList();

          if (stagesTypes.Count > 1)
          {
            // Используем foreach, так как Linq не работает с такой конструкцией.
            foreach (var stage in stagesTypes)
            {
              if (stage == Docflow.ApprovalRuleBaseStages.StageType.Register)
              {
                block.AssignIcon(OfficialDocuments.Info.Actions.Register, iconSize);
                break;
              }
              
              if (stage == Docflow.ApprovalRuleBaseStages.StageType.Print)
              {
                block.AssignIcon(ApprovalTasks.Resources.Print, iconSize);
                break;
              }
              
              if (stage == Docflow.ApprovalRuleBaseStages.StageType.Execution)
              {
                block.AssignIcon(ApprovalTasks.Resources.Completed, iconSize);
                break;
              }
            }
          }
          else
            block.AssignIcon(ApprovalTasks.Resources.Completed, iconSize);
        }
      }
      
      // Создание поручений выполнено.
      if (ApprovalExecutionAssignments.Is(assignment) &&
          (assignment.Result == Docflow.ApprovalExecutionAssignment.Result.Complete))
      {
        block.AssignIcon(ApprovalTasks.Resources.Completed, iconSize);
      }
      
      // Прекращено, не подписано контрагентом.
      if ((!ApprovalReviewAssignments.Is(assignment) && assignment.Result == Docflow.ApprovalSigningAssignment.Result.Abort) ||
          assignment.Result == Docflow.ApprovalCheckReturnAssignment.Result.NotSigned)
      {
        block.AssignIcon(StateBlockIconType.Abort, iconSize);
      }
    }
    
    /// <summary>
    /// Добавить комментарий к блоку.
    /// </summary>
    /// <param name="block">Блок.</param>
    /// <param name="assignment">Задание.</param>
    /// <param name="isResolutionBlock">Признак: блок резолюции или нет.</param>
    private void AddComment(StateBlock block, IAssignment assignment, bool isResolutionBlock)
    {
      var comment = Functions.Module.GetAssignmentUserComment(assignment);
      
      if (assignment.Status != Workflow.AssignmentBase.Status.Completed)
        return;
      
      if (ApprovalReviewAssignments.Is(assignment))
      {
        // Для блока резолюции добавить информацию по поручениям.
        if (isResolutionBlock && assignment.Result == Docflow.ApprovalReviewAssignment.Result.AddActionItem)
        {
          var actionItems = Functions.Module.GetActionItemsForResolution(assignment.Task, Workflow.Task.Status.Draft, ApprovalTasks.As(assignment.Task).Addressee);
          if (actionItems.Any())
          {
            block.AddLineBreak();
            block.AddLabel(Constants.Module.SeparatorText, Docflow.PublicFunctions.Module.CreateSeparatorStyle());
            
            // Добавить информацию по каждому поручению.
            foreach (var actionItem in actionItems)
            {
              AddActionItemInfo(block, actionItem);
            }
            return;
          }
        }
        
        // Для рассмотрения добавить комментарий "Принято к сведению", если его нет.
        if (assignment.Result == Docflow.ApprovalReviewAssignment.Result.Informed &&
            string.IsNullOrWhiteSpace(comment))
          comment = ApprovalTasks.Resources.Informed;
        
        // Для рассмотрения добавить комментарий "Отправлено на исполнение", если его нет.
        if (assignment.Result == Docflow.ApprovalReviewAssignment.Result.AddActionItem &&
            string.IsNullOrWhiteSpace(comment))
          comment = ApprovalTasks.Resources.SentForExecution;
      }
      
      if (!string.IsNullOrWhiteSpace(comment))
      {
        block.AddLineBreak();
        block.AddLabel(Constants.Module.SeparatorText, Docflow.PublicFunctions.Module.CreateSeparatorStyle());
        block.AddLineBreak();
        block.AddEmptyLine(Constants.Module.EmptyLineMargin);
        if (isResolutionBlock)
          block.AddLabel(Docflow.Functions.Module.TrimEndNewLines(comment));
        else
          block.AddLabel(Docflow.Functions.Module.TrimEndNewLines(comment), Functions.Module.CreateNoteStyle());
      }
    }
    
    /// <summary>
    /// Добавить информацию о созданном поручении в резолюцию.
    /// </summary>
    /// <param name="block">Блок.</param>
    /// <param name="actionItem">Поручение.</param>
    public static void AddActionItemInfo(Sungero.Core.StateBlock block, ITask actionItem)
    {
      var infos = Functions.Module.ActionItemInfoProvider(actionItem).ToArray();
      
      if (infos.Length == 0)
        return;
      
      block.AddEmptyLine(Constants.Module.EmptyLineMargin);
      
      // Отчет пользователя.
      block.AddLabel(Docflow.PublicFunctions.Module.GetFormatedUserText(infos[0]));
      block.AddLineBreak();
      
      // Исполнители.
      var performerStyle = Sungero.Docflow.PublicFunctions.Module.CreatePerformerDeadlineStyle();
      var info = string.Empty;
      info += infos[1];
      
      // Срок.
      info += infos[2];
      
      // Контролер.
      info += infos[3];
      
      block.AddLabel(info, performerStyle);
      block.AddLineBreak();
      block.AddLineBreak();
    }
    
    /// <summary>
    /// Установить иконку группы заданий на согласование.
    /// </summary>
    /// <param name="block">Блок.</param>
    /// <param name="isApproved">Группа согласована.</param>
    /// <param name="hasRework">Имеются ли доработки.</param>
    /// <param name="nextAssignment">Следующее задание.</param>
    /// <param name="hasAbort">Признак: было ли прекращение в группе.</param>
    /// <param name="hasTaskAbort">Признак: прекращена ли задача.</param>
    /// <param name="iconSize">Размер иконки.</param>
    /// <returns>Статус группы согласований.</returns>
    private string SetGroupIconAndGetGroupStatus(StateBlock block, bool isApproved, bool hasRework, IAssignment nextAssignment,
                                                 bool hasAbort, bool hasTaskAbort, StateBlockIconSize iconSize)
    {
      // Установить иконку "В работе".
      block.AssignIcon(ApprovalTasks.Resources.ApproveStage, iconSize);
      var status = ApprovalTasks.Resources.StateViewInProcess;
      
      // Установить иконку доработки, если была хоть одна.
      if (hasRework)
      {
        block.AssignIcon(ApprovalTasks.Resources.Notapprove, iconSize);
        return ApprovalTasks.Resources.StateViewNotApproved;
      }
      
      // Установить иконку "Выполнено", если все согласны.
      if (isApproved)
      {
        block.AssignIcon(ApprovalTasks.Resources.Approve, iconSize);
        return ApprovalTasks.Resources.StateViewEndorsed;
      }
      
      if (!hasAbort)
        return status;
      
      // Если есть прекращенные задания и доработка не была выполнена, то круг был прекращен.
      if (nextAssignment != null && !nextAssignment.Result.HasValue &&
          Equals(nextAssignment.BlockUid, Constants.Module.ApprovalReworkAssignmentBlockUid))
      {
        block.AssignIcon(StateBlockIconType.Abort, iconSize);
        return ApprovalTasks.Resources.StateViewAborted;
      }
      
      // Если следующего задания нет и задача прекращена, то круг прекращен.
      if (nextAssignment == null &&
          (_obj.Status == Workflow.Task.Status.Aborted || _obj.Status == Workflow.Task.Status.Suspended))
      {
        block.AssignIcon(StateBlockIconType.Abort, iconSize);
        return ApprovalTasks.Resources.StateViewAborted;
      }
      
      // Если между созданием текущей и следующей задачи было прекращение, то круг был прекращен.
      if (nextAssignment != null && hasTaskAbort)
      {
        block.AssignIcon(StateBlockIconType.Abort, iconSize);
        return ApprovalTasks.Resources.StateViewAborted;
      }
      
      return status;
    }
    
    #endregion
    
    #region Регламент
    
    /// <summary>
    /// Построить регламент.
    /// </summary>
    /// <returns>Регламент.</returns>
    [Remote(IsPure = true)]
    public Sungero.Core.StateView GetStagesStateView()
    {
      var approvers = _obj.AddApprovers.Select(a => a.Approver).ToList();
      if (_obj.Status != Sungero.Docflow.ApprovalTask.Status.Draft)
        approvers = _obj.AddApproversExpanded.Select(a => a.Approver).ToList();
      return PublicFunctions.ApprovalRuleBase.GetStagesStateView(_obj, approvers, _obj.Signatory, _obj.Addressee, _obj.DeliveryMethod, _obj.ExchangeService, true);
    }
    
    #endregion
    
    #region Проверка на прочитанность документа
    
    /// <summary>
    /// Получить время, когда пользователь последний раз видел тело последней версии документа.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="employee">Пользователь.</param>
    /// <returns>Время.</returns>
    [Public]
    public static DateTime? GetDocumentLastViewDate(IElectronicDocument document, IUser employee)
    {
      var lastVersionNumber = document.Versions.Max(v => v.Number);
      return document.History.GetAll()
        .Where(h => h.User.Equals(Users.Current) && h.VersionNumber == lastVersionNumber)
        .Where(h =>
               (h.Action == CoreEntities.History.Action.Update && h.Operation == Content.DocumentHistory.Operation.UpdateVerBody) ||
               (h.Action == CoreEntities.History.Action.Update && h.Operation == Content.DocumentHistory.Operation.Import) ||
               (h.Action == CoreEntities.History.Action.Update && h.Operation == Content.DocumentHistory.Operation.CreateVersion) ||
               (h.Action == CoreEntities.History.Action.Create && h.Operation == null) ||
               (h.Action == CoreEntities.History.Action.Read && h.Operation == Content.DocumentHistory.Operation.ReadVerBody) ||
               (h.Action == CoreEntities.History.Action.Read && h.Operation == Content.DocumentHistory.Operation.Export))
        .Max(h => h.HistoryDate);
    }
    
    /// <summary>
    /// Был ли обновлен документ с момента последнего просмотра.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>True, если пользователь не видел актуальное содержимое документа, иначе false.</returns>
    [Public, Remote(IsPure = true)]
    public static bool DocumentHasBodyUpdateAfterLastView(Sungero.Content.IElectronicDocument document)
    {
      if (!document.HasVersions)
        return false;
      
      var lastVersionNumber = document.Versions.Max(v => v.Number);
      var lastViewDate = GetDocumentLastViewDate(document, Users.Current);

      // С момента последнего просмотра мной, были ли изменения другими этой версии.
      return lastViewDate == null ||
        document.History.GetAll().Any(
          h => !h.User.Equals(Users.Current) &&
          h.HistoryDate > lastViewDate &&
          h.VersionNumber == lastVersionNumber &&
          ((h.Action == CoreEntities.History.Action.Update && h.Operation == Content.DocumentHistory.Operation.UpdateVerBody) ||
           (h.Action == CoreEntities.History.Action.Update && h.Operation == Content.DocumentHistory.Operation.Import) ||
           (h.Action == CoreEntities.History.Action.Update && h.Operation == Content.DocumentHistory.Operation.CreateVersion) ||
           (h.Action == CoreEntities.History.Action.Create && h.Operation == null)));
    }
    
    /// <summary>
    /// Был ли документ просмотрен сотрудником.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>True, если пользователь просматривал документ, иначе false.</returns>
    [Remote(IsPure = true)]
    public static bool DocumenHasBeenViewed(Sungero.Content.IElectronicDocument document)
    {
      if (!document.HasVersions)
        return false;
      return GetDocumentLastViewDate(document, Users.Current) != null;
    }
    
    #endregion
    
    #region Схлопывание
    
    /// <summary>
    /// Получить список типов этапов схлопывания.
    /// </summary>
    /// <returns>Список типов этапов, которые можно схлопнуть.</returns>
    public static List<Enumeration> GetAvailableToCollapseStageTypes()
    {
      return new List<Enumeration>()
      {
        Docflow.ApprovalStage.StageType.Sending,
        Docflow.ApprovalStage.StageType.Print,
        Docflow.ApprovalStage.StageType.Register,
        Docflow.ApprovalStage.StageType.Execution,
        Docflow.ApprovalStage.StageType.Sign,
        Docflow.ApprovalStage.StageType.Review
      };
    }
    
    /// <summary>
    /// Определить, нужно ли схлапывание для этапа задачи.
    /// </summary>
    /// <param name="task">Задача на согласование.</param>
    /// <param name="stage">Этап согласования.</param>
    /// <returns>True, если этап схлапывается.</returns>
    public static bool NeedCollapse(IApprovalTask task, Structures.Module.DefinedApprovalStageLite stage)
    {
      var mayCollapsedStageTypes = GetAvailableToCollapseStageTypes();

      var collapsedStages = GetCollapsedStages(task, stage);
      if (collapsedStages == null || !collapsedStages.Any(s => !Equals(s, stage)))
        return false;
      
      var mainStageTypeIndex = collapsedStages.Max(s => mayCollapsedStageTypes.IndexOf(s.Stage.StageType.Value));
      var mainStageType = mayCollapsedStageTypes[mainStageTypeIndex];
      
      if (!Equals(mainStageType, stage.StageType))
        return true;
      
      return false;
    }
    
    /// <summary>
    /// Получить схлопываемые этапы.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Схлапываемые этапы.</returns>
    public static List<Structures.Module.DefinedApprovalStageLite> GetCollapsedStages(IApprovalTask task, Structures.Module.DefinedApprovalStageLite stage)
    {
      // Этап точно не схлопнется, если он не схлапываемого типа.
      var mayCollapsedStageTypes = GetAvailableToCollapseStageTypes();
      if (!mayCollapsedStageTypes.Contains(stage.StageType.Value))
        return new List<Structures.Module.DefinedApprovalStageLite>() { stage };
      
      // Получить этапы, которые могут быть схлопнуты с текущим.
      var stagePerformer = Docflow.PublicFunctions.ApprovalStage.GetStagePerformer(task, stage.Stage, null, null);
      var stages = Functions.ApprovalTask.GetStages(task).Stages;
      var mayCollapsedStages = stages
        .Where(s => mayCollapsedStageTypes.Any(t => Equals(t, s.StageType)))
        .Where(s => Equals(Docflow.PublicFunctions.ApprovalStage.GetStagePerformer(task, s.Stage, null, null), stagePerformer))
        .ToList();
      
      // Если схлопывать этап не с чем, то вернуть список с одним исходным этапом.
      if (!mayCollapsedStages.Any(s => !Equals(s, stage)))
        return mayCollapsedStages;
      
      // Найти границы доступных для схлапывания этапов.
      var currentStageIndex = stages.IndexOf(stage);
      var firstStageIndex = stages.IndexOf(mayCollapsedStages.OrderBy(x => stages.IndexOf(x)).First());
      var lastStageIndex = stages.IndexOf(mayCollapsedStages.OrderByDescending(x => stages.IndexOf(x)).First());
      
      // Получить список схлапываемых этапов. Не схлопывать "бумажные" этапы рассмотрения и подписания с предыдущими.
      var collapsedStages = new List<Structures.Module.DefinedApprovalStageLite>();
      for (var i = firstStageIndex; i <= lastStageIndex; ++i)
      {
        var ruleStage = stages[i];
        var stageMayCollapsed = mayCollapsedStages.Contains(ruleStage);
        
        // Добавить в схлапываемые, если он входит в доступные для схлапывания этапы.
        if (stageMayCollapsed &&
            (ruleStage.StageType != StageType.Sign || ruleStage.Stage.IsConfirmSigning != true) &&
            (ruleStage.StageType != StageType.Review || ruleStage.Stage.IsResultSubmission != true))
        {
          collapsedStages.Add(ruleStage);
          continue;
        }
        
        // Закончить поиск этапов, если искомый этап в списке схлапываемых.
        if (collapsedStages.Contains(stage))
          break;
        
        // Очистить список схлопываемых этапов, если в нём нет искомого этапа.
        collapsedStages.Clear();
        
        // Добавить этап после очистки, если он входит в доступные для схлапывания.
        if (stageMayCollapsed)
          collapsedStages.Add(ruleStage);
      }
      
      // Если в списке схлапываемых этапов есть создание поручений, то убрать его, если оно не нужно.
      var executionStage = collapsedStages.FirstOrDefault(s => s.Stage.StageType == StageType.Execution);
      if (executionStage != null)
      {
        var isExecutionNeeded = Functions.ApprovalExecutionAssignment.NeedExecutionAssignment(task);
        if (!isExecutionNeeded && stage.Stage.StageType == StageType.Execution)
          return new List<Structures.Module.DefinedApprovalStageLite>() { stage };
        
        if (!isExecutionNeeded)
          collapsedStages.Remove(executionStage);
      }
      
      // Если отправка не требуется, исключить ее из схлапываемых этапов.
      var sendingStage = collapsedStages.FirstOrDefault(x => x.StageType == StageType.Sending);
      var hasSigningStage = collapsedStages.Any(s => s.StageType == StageType.Sign);
      if (sendingStage != null && Functions.ApprovalTask.NeedSkipSendingStage(task, hasSigningStage))
        collapsedStages.Remove(sendingStage);
      
      return collapsedStages;
    }
    
    /// <summary>
    /// Определить схлопнутый срок в днях.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Число дней.</returns>
    public static int? CollapsedDeadlineInDays(IApprovalTask task, Structures.Module.DefinedApprovalStageLite stage)
    {
      var collapsedStages = GetCollapsedStages(task, stage);
      if (!collapsedStages.Any(s => !Equals(s, stage)))
        return stage.Stage.DeadlineInDays;
      
      var deadline = collapsedStages.Select(s => s.Stage).Sum(s => s.DeadlineInDays);
      Logger.DebugFormat("CollapsedDeadlineInDays: Task {0}, stage{1}:{2}, deadline = {3}.", task.Id, stage.Number, stage.StageType, deadline);
      return deadline;
    }
    
    /// <summary>
    /// Определить схлопнутый срок в часах.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Число часов.</returns>
    public static int? CollapsedDeadlineInHours(IApprovalTask task, Structures.Module.DefinedApprovalStageLite stage)
    {
      var collapsedStages = GetCollapsedStages(task, stage);
      if (!collapsedStages.Any(s => !Equals(s, stage)))
        return stage.Stage.DeadlineInHours;
      
      var deadline = collapsedStages.Select(s => s.Stage).Sum(s => s.DeadlineInHours);
      Logger.DebugFormat("CollapsedDeadlineInHours: Task {0}, stage{1}:{2}, deadline = {3}.", task.Id, stage.Number, stage.StageType, deadline);
      return deadline;
    }
    
    /// <summary>
    /// Получить схлопнутую тему задания.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Исполнитель.</returns>
    public static string GetCollapsedSubject(IApprovalTask task, Structures.Module.DefinedApprovalStageLite stage)
    {
      var subject = string.Empty;
      var collapsedStageTypes = GetCollapsedStagesTypes(task, stage).Distinct();
      var document = task.DocumentGroup.OfficialDocuments.First();
      
      // Сформировать тему, следуя порядку этапов в правиле.
      foreach (var stageType in collapsedStageTypes)
      {
        // Регистрация.
        if (stageType == Docflow.ApprovalStage.StageType.Register)
          subject = string.Join(", ", subject, ApprovalTasks.Resources.RegistrationAsgSubject);

        // Печать.
        if (stageType == Docflow.ApprovalStage.StageType.Print)
        {
          var lastCollapsedStage = GetCollapsedStages(task, stage).LastOrDefault();
          var nextStage = Functions.ApprovalRuleBase.GetNextStage(task.ApprovalRule, document, lastCollapsedStage, task);
          if (nextStage != null)
          {
            var nextStageType = nextStage.Stage.StageType;
            var needSkipNextSignStage = Functions.ApprovalTask.NeedSkipSignStage(task, nextStage, task.Signatory, task.Addressee);
            
            // Если следующий этап - подписание, то указать в теме необходимость передать на подписание.
            if ((nextStageType == Docflow.ApprovalStage.StageType.Sign ||
                 nextStageType == Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ConfirmSign) &&
                !needSkipNextSignStage)
            {
              subject = string.Join(", ", subject, ApprovalTasks.Resources.PrintAndTransferAsgSubject);
            }
            else if (nextStageType == Docflow.ApprovalStage.StageType.Review ||
                     nextStageType == Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ReviewingResult ||
                     needSkipNextSignStage)
            {
              // Если следующий этап - рассмотрение, то указать в теме необходимость передать на рассмотрение.
              subject = string.Join(", ", subject, ApprovalTasks.Resources.PrintAndTransferToReviewAsgSubject);
            }
            else
              subject = string.Join(", ", subject, ApprovalTasks.Resources.PrintAsgSubject);
          }
          else
            subject = string.Join(", ", subject, ApprovalTasks.Resources.PrintAsgSubject);
        }
        
        // Подветрждение подписания.
        if (stageType == Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ConfirmSign)
        {
          subject = string.Join(", ", subject, ApprovalTasks.Resources.ConfirmSigningSubject);
        }
        
        // Подписание.
        if (stageType == Docflow.ApprovalStage.StageType.Sign)
        {
          subject = string.Join(", ", subject, ApprovalTasks.Resources.SignAsgSubject);
        }

        // Отправка КА.
        if (stageType == Docflow.ApprovalStage.StageType.Sending)
        {
          if (document.InternalApprovalState == Docflow.OfficialDocument.InternalApprovalState.Signed ||
              collapsedStageTypes.Contains(Docflow.ApprovalStage.StageType.Sign) ||
              collapsedStageTypes.Contains(Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ConfirmSign) ||
              document.ExternalApprovalState != Docflow.OfficialDocument.ExternalApprovalState.Signed)
            subject = string.Join(", ", subject, ApprovalTasks.Resources.SendToCounterparty);
        }
        
        // Рассмотрение.
        if (stageType == Docflow.ApprovalStage.StageType.Review)
        {
          subject = string.Join(", ", subject, ApprovalTasks.Resources.ReviewAsgSubject);
        }
        
        // Обработка резолюции.
        if (stageType == Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ReviewingResult)
        {
          subject = string.Join(", ", subject, ApprovalTasks.Resources.SpecifyReviewingResultAsgSubject);
        }
        
        // Создание поручений.
        if (stageType == Docflow.ApprovalStage.StageType.Execution)
        {
          subject = string.Join(", ", subject, ApprovalTasks.Resources.ExecutionAsgSubject);
        }
      }
      
      subject = Functions.Module.ReplaceFirstSymbolToUpperCase(subject.TrimStart(new[] { ',', ' ' }));
      return Docflow.PublicFunctions.Module.TrimSpecialSymbols("{0}: {1}", subject, task.DocumentGroup.OfficialDocuments.First().Name);
    }
    
    /// <summary>
    /// Получить схлопнутую тему задания.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Исполнитель.</returns>
    public static string GetCollapsedThreadSubject(IApprovalTask task, Structures.Module.DefinedApprovalStageLite stage)
    {
      var subject = string.Empty;
      var collapsedStageTypes = GetCollapsedStagesTypes(task, stage).Distinct();
      var document = task.DocumentGroup.OfficialDocuments.First();
      
      // Сформировать тему, следуя порядку этапов в правиле.
      foreach (var stageType in collapsedStageTypes)
      {
        // Регистрация.
        if (stageType == Docflow.ApprovalStage.StageType.Register)
          subject = string.Join(", ", subject, Docflow.ApprovalRegistrationAssignments.Info.LocalizedName);

        // Печать.
        if (stageType == Docflow.ApprovalStage.StageType.Print)
        {
          subject = string.Join(", ", subject, Docflow.ApprovalPrintingAssignments.Info.LocalizedName);
        }
        
        // Подветрждение подписания.
        if (stageType == Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ConfirmSign)
        {
          subject = string.Join(", ", subject, ApprovalTasks.Resources.ConfirmSigningThreadSubject);
        }
        
        // Подписание.
        if (stageType == Docflow.ApprovalStage.StageType.Sign)
        {
          subject = string.Join(", ", subject, Docflow.ApprovalSigningAssignments.Info.LocalizedName);
        }

        // Отправка КА.
        if (stageType == Docflow.ApprovalStage.StageType.Sending)
        {
          if (document.InternalApprovalState == Docflow.OfficialDocument.InternalApprovalState.Signed ||
              collapsedStageTypes.Contains(Docflow.ApprovalStage.StageType.Sign) ||
              collapsedStageTypes.Contains(Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ConfirmSign) ||
              document.ExternalApprovalState != Docflow.OfficialDocument.ExternalApprovalState.Signed)
            subject = string.Join(", ", subject, Docflow.ApprovalSendingAssignments.Info.LocalizedName);
        }
        
        // Рассмотрение.
        if (stageType == Docflow.ApprovalStage.StageType.Review)
        {
          subject = string.Join(", ", subject, Docflow.ApprovalReviewAssignments.Info.LocalizedName);
        }
        
        // Обработка резолюции.
        if (stageType == Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ReviewingResult)
        {
          subject = string.Join(", ", subject, ApprovalTasks.Resources.SpecifyReviewingResultAsgThreadSubject);
        }
        
        // Создание поручений.
        if (stageType == Docflow.ApprovalStage.StageType.Execution)
        {
          subject = string.Join(", ", subject, Docflow.ApprovalExecutionAssignments.Info.LocalizedName);
        }
      }
      subject = subject.ToLower();
      subject = Functions.Module.ReplaceFirstSymbolToUpperCase(subject.TrimStart(new[] { ',', ' ' }));
      return subject;
    }
    
    /// <summary>
    /// Получить список схлопнутых типов этапов.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Список схлопнутых этапов.</returns>
    public static List<Enumeration?> GetCollapsedStagesTypes(IApprovalTask task, Structures.Module.DefinedApprovalStageLite stage)
    {
      var stagesTypes = new List<Enumeration?>() { };
      var collapsedStages = GetCollapsedStages(task, stage);
      
      // Сформировать список типов этапов, следуя порядку в правиле.
      foreach (var collapsedStage in collapsedStages)
      {
        var stageType = collapsedStage.Stage.StageType;
        
        // Для подтверждения подписания указать это.
        if (stageType == Docflow.ApprovalStage.StageType.Sign)
        {
          var confirmBy = Functions.ApprovalStage.GetConfirmByForSignatory(collapsedStage.Stage, task.Signatory, task);
          if (confirmBy != null)
          {
            stagesTypes.Add(Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ConfirmSign);
            continue;
          }
        }
        
        // Для обработки резолюции указать это.
        if (stageType == Docflow.ApprovalStage.StageType.Review)
        {
          var assistant = Functions.ApprovalStage.GetAddresseeAssistantForResultSubmission(collapsedStage.Stage, task.Addressee, task);
          if (assistant != null)
          {
            stagesTypes.Add(Docflow.ApprovalSendingAssignmentCollapsedStagesTypesSen.StageType.ReviewingResult);
            continue;
          }
        }
        
        stagesTypes.Add(stageType);
      }
      
      return stagesTypes.Distinct().ToList();
    }
    
    /// <summary>
    /// Получить схлопнутый результат выполнения задания.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="result">Результат подписания.</param>
    /// <returns>Исполнитель.</returns>
    public static CommonLibrary.LocalizedString GetCollapsedResult(IApprovalTask task, Enumeration? result)
    {
      var resultText = Sungero.Commons.Resources.Empty;
      var baseStage = task.ApprovalRule.Stages.FirstOrDefault(s => s.Number == task.StageNumber);
      var stage = Structures.Module.DefinedApprovalStageLite.Create(baseStage.Stage, baseStage.Number, baseStage.StageType);
      var collapsedStageTypes = GetCollapsedStages(task, stage).Select(s => s.StageType).Distinct();
      
      // Сформировать результат, следуя порядку этапов в правиле.
      foreach (var stageType in collapsedStageTypes)
      {
        // Регистрация.
        if (stageType == Docflow.ApprovalStage.StageType.Register)
        {
          var document = task.DocumentGroup.OfficialDocuments.First();
          var registrationNumber = document.RegistrationNumber;
          var documentRegister = document.DocumentRegister;
          if (registrationNumber != null && documentRegister != null)
          {
            var registerName = documentRegister.DisplayName;
            if (registerName.Length > 50)
              registerName = string.Format("{0}…", registerName.Substring(0, 50));
            resultText.AppendLine(ApprovalTasks.Resources.DocumentRegisteredFromNumberInDocumentRegisterFormat(registrationNumber, registerName));
          }
        }

        // Печать.
        if (stageType == Docflow.ApprovalStage.StageType.Print)
          resultText.AppendLine(ApprovalTasks.Resources.DocumentPrinted);
        
        // Подписание.
        if (stageType == Docflow.ApprovalStage.StageType.Sign)
        {
          if (result == Docflow.ApprovalSigningAssignment.Result.Sign)
            resultText.AppendLine(ApprovalTasks.Resources.DocumentSigned);
          else if (result == Docflow.ApprovalSigningAssignment.Result.ConfirmSign)
            resultText.AppendLine(ApprovalTasks.Resources.DocumentSigningConfirmed);
          else if (collapsedStageTypes.Any(s => s == StageType.Review))
            resultText.AppendLine(ApprovalTasks.Resources.DocumentSigned);
          else if (result == Docflow.ApprovalSigningAssignment.Result.ForRevision)
            return ApprovalTasks.Resources.ForRework;
          else
            return ApprovalTasks.Resources.DeniedToSignDocument;
        }
        
        // Отправка КА.
        if (stageType == Docflow.ApprovalStage.StageType.Sending)
        {
          var document = task.DocumentGroup.OfficialDocuments.First();
          if (document.InternalApprovalState == Docflow.OfficialDocument.InternalApprovalState.Signed ||
              collapsedStageTypes.Contains(Docflow.ApprovalStage.StageType.Sign) ||
              document.ExternalApprovalState != Docflow.OfficialDocument.ExternalApprovalState.Signed)
            resultText.AppendLine(ApprovalTasks.Resources.DocumentSended);
        }
        
        // Рассмотрение.
        if (stageType == Docflow.ApprovalStage.StageType.Review)
        {
          var assistant = Functions.ApprovalStage.GetAddresseeAssistantForResultSubmission(stage.Stage, task.Addressee, task);

          if (result == ReviewResults.AddResolution)
          {
            // Сменить результат выполнения, если это внесение результата рассмотрения.
            var resultString = assistant != null ? ApprovalTasks.Resources.PassedResolutionEntered : ApprovalTasks.Resources.ResolutionPassed;
            resultText.AppendLine(resultString);
          }
          else if (result == ReviewResults.AddActionItem)
          {
            resultText.AppendLine(ApprovalTasks.Resources.SentForExecution);
          }
          else if (result == ReviewResults.Informed)
          {
            // Сменить результат выполнения, если это внесение результата рассмотрения.
            var resultString = assistant != null ? ApprovalTasks.Resources.InformedResultEntered : ApprovalTasks.Resources.Informed;
            resultText.AppendLine(resultString);
          }
          else if (result == ReviewResults.ForRework)
          {
            return ApprovalTasks.Resources.ForRework;
          }
          else
          {
            return ApprovalTasks.Resources.DeniedToReviewDocument;
          }
        }
        
        // Создание поручений.
        if (stageType == Docflow.ApprovalStage.StageType.Execution)
        {
          // Для схлопнутых заданий не выводить результат создания поручений.
          if (collapsedStageTypes.Count() == 1)
            resultText.AppendLine(ApprovalTasks.Resources.Done);
        }
      }
      
      return resultText;
    }
    
    #endregion
    
    #region Проверка схлопнутости этапа с другим
    
    /// <summary>
    /// Проверить, схлапывается ли текущий этап с указанным типом этапа.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="currentStage">Текущий этап.</param>
    /// <param name="specificStageType">Целевой тип.</param>
    /// <returns>True, если схлопнут, иначе false.</returns>
    [Remote(IsPure = true)]
    public static bool CurrentStageCollapsedWithSpecificStage(IApprovalTask task, Structures.Module.DefinedApprovalStageLite currentStage, Enumeration specificStageType)
    {
      if (currentStage == null)
        return false;
      
      var collapsedStages = GetCollapsedStages(task, currentStage);
      return collapsedStages.Any(s => s.StageType == specificStageType);
    }
    
    /// <summary>
    /// Проверить, схлапывается ли текущий этап с указанным типом этапа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="currentStageNumber">Текущий номер этапа.</param>
    /// <param name="specificStageType">Целевой тип.</param>
    /// <returns>True, если схлопнут, иначе false.</returns>
    [Remote(IsPure = true)]
    public static bool CurrentStageCollapsedWithSpecificStage(IApprovalTask task, int? currentStageNumber, Enumeration specificStageType)
    {
      var currentStage = task.ApprovalRule.Stages.Where(s => s.Number == currentStageNumber).FirstOrDefault();
      if (currentStage != null)
      {
        var stage = Structures.Module.DefinedApprovalStageLite.Create(currentStage.Stage, currentStage.Number, currentStage.StageType);
        return CurrentStageCollapsedWithSpecificStage(task, stage, specificStageType);
      }
      return false;
    }
    
    #endregion
    
    #region Пропуск этапов
    
    /// <summary>
    /// Необходимо ли пропустить этап подписания.
    /// </summary>
    /// <param name="stage">Запись этапа в правиле.</param>
    /// <param name="signatory">Подписывающий.</param>
    /// <param name="addressee">Адресат.</param>
    /// <returns>True, если необходимо, иначе false.</returns>
    public bool NeedSkipSignStage(Structures.Module.DefinedApprovalStageLite stage,
                                  Sungero.Company.IEmployee signatory,
                                  Sungero.Company.IEmployee addressee)
    {
      if (stage.Stage.StageType != Sungero.Docflow.ApprovalStage.StageType.Sign)
        return false;
      
      // Пропустить, если следующим этапом идет рассмотрение и исполнитель совпадает.
      var document = _obj.DocumentGroup.OfficialDocuments.FirstOrDefault();
      var nextStageNumber = Functions.ApprovalRuleBase.GetNextStageNumber(_obj.ApprovalRule, document, stage.Number, _obj);
      if (nextStageNumber == null || !nextStageNumber.Number.HasValue)
        return false;

      var reviewStage = _obj.ApprovalRule.Stages
        .Where(s => s.Number == nextStageNumber.Number.Value)
        .Where(s => s.StageType == Docflow.ApprovalRuleBaseStages.StageType.Review)
        .FirstOrDefault();
      
      if (reviewStage == null)
        return false;

      signatory = signatory ?? Functions.ApprovalStage.GetStagePerformer(_obj, stage.Stage);
      addressee = addressee ?? Functions.ApprovalStage.GetStagePerformer(_obj, reviewStage.Stage);
      if (Equals(signatory, addressee))
        return true;
      
      return false;
    }
    
    /// <summary>
    /// Необходимо ли пропустить этап отправки контрагенту.
    /// </summary>
    /// <param name="isCollapsedWithSigning">Схлопнут ли текущий этап с подписанием.</param>
    /// <returns>True, если необходимо, иначе false.</returns>
    public bool NeedSkipSendingStage(bool isCollapsedWithSigning)
    {
      var document = _obj.DocumentGroup.OfficialDocuments.First();
      
      // Если КА в МКДО ожидает подписи от нас или уже подписан двумя сторонами, то контроль возврата не нужен.
      if ((document.ExchangeState == Docflow.OfficialDocument.ExchangeState.SignRequired ||
           document.ExchangeState == Docflow.OfficialDocument.ExchangeState.Signed) &&
          !(document.InternalApprovalState == Docflow.OfficialDocument.InternalApprovalState.Signed ||
            isCollapsedWithSigning))
        return true;
      
      return document.ExternalApprovalState == Docflow.OfficialDocument.ExternalApprovalState.Signed &&
        !(document.InternalApprovalState == Docflow.OfficialDocument.InternalApprovalState.Signed ||
          isCollapsedWithSigning);
    }
    
    #endregion
    
    #region Выдача документа
    
    /// <summary>
    /// Обновить статус согласования документа и добавить записи о выдаче документа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="returnResponcibleID">Ответственный за возврат.</param>
    public static void IssueDocument(IApprovalTask task, int returnResponcibleID)
    {
      // Статус документа и выдачу обновить, только если этап схлопнут с отправкой контрагенту.
      if (!CurrentStageCollapsedWithSpecificStage(task, task.StageNumber, Docflow.ApprovalStage.StageType.Sending))
        return;
      
      // Подписан нами: на согласовании, оба оригинала у КА.
      // Двумя сторонами: статус не меняем, оригинал у КА.
      var returnStage = Functions.ApprovalTask.GetStages(task).Stages.FirstOrDefault(s => s.StageType == Docflow.ApprovalStage.StageType.CheckReturn);
      var document = task.DocumentGroup.OfficialDocuments.First();
      if (document.ExternalApprovalState == Docflow.OfficialDocument.ExternalApprovalState.Signed &&
          document.InternalApprovalState != InternalApprovalState.Signed)
        return;
      
      if (returnStage != null)
      {
        var recipients = returnStage.Stage.Recipients.Where(a => a.Recipient != null).Select(b => b.Recipient).ToList<IRecipient>();
        if (recipients.Any())
        {
          var responcibleEmployee = Sungero.Docflow.Functions.Module.GetEmployeesFromRecipients(recipients).FirstOrDefault();
          if (responcibleEmployee != null)
            returnResponcibleID = responcibleEmployee.Id;
        }
        else
        {
          var roles = returnStage.Stage.ApprovalRoles.Where(r => r.ApprovalRole != null);
          if (roles.Any())
          {
            var roleEmployee = Functions.ApprovalRoleBase.GetRolePerformer(roles.First().ApprovalRole, task);
            if (roleEmployee != null)
              returnResponcibleID = roleEmployee.Id;
          }
        }
      }
      
      // Если документ отправлен через сервис обмена и ответ еще не получен, не создаем новые записи выдачи.
      var trackings = document.Tracking.Where(x => x.ReturnResult == null && x.ReturnTask == null && x.ExternalLinkId != null).ToList();
      var tracking = trackings.Where(x => x.Action == Docflow.OfficialDocumentTracking.Action.Endorsement).LastOrDefault();
      if (tracking != null)
      {
        tracking.ReturnTask = task;
        
        // Переписать ответственного за возврат.
        if (returnStage != null)
          tracking.DeliveredTo = Sungero.Company.Employees.Get(returnResponcibleID);
        
        // Обработка приложений, отправленных через сервис обмена.
        foreach (var addendum in task.AddendaGroup.OfficialDocuments.Where(x => x.Tracking.Any(t => t.ReturnResult == null && t.ReturnTask == null && t.ExternalLinkId != null)).ToList())
        {
          var addendumTracking = addendum.Tracking.LastOrDefault(x => x.ReturnResult == null && x.ReturnTask == null && x.ExternalLinkId != null && x.Action == Docflow.OfficialDocumentTracking.Action.Endorsement);
          
          if (addendumTracking != null)
          {
            addendumTracking.ReturnTask = task;
            
            if (returnStage != null)
              addendumTracking.DeliveredTo = tracking.DeliveredTo;
          }
        }
      }
      
      if (trackings.Any())
        return;

      // Если документ не подписан нами на момент отправки, то отправить 2 экземпляра с возвратом.
      if (returnStage != null && document.InternalApprovalState == InternalApprovalState.OnApproval)
        Functions.ApprovalTask.IssueDocumentToCounterparty(task, document, returnStage.Stage.DeadlineInDays, returnResponcibleID, Docflow.OfficialDocumentTracking.Action.Endorsement);
      else
        Functions.ApprovalTask.IssueDocumentToCounterparty(null, document, null, returnResponcibleID, Docflow.OfficialDocumentTracking.Action.Sending);
      
      if (returnStage != null && document.ExternalApprovalState != ExternalApprovalState.Signed)
        Functions.ApprovalTask.IssueDocumentToCounterparty(task, document, returnStage.Stage.DeadlineInDays, returnResponcibleID, Docflow.OfficialDocumentTracking.Action.Endorsement);
    }
    
    /// <summary>
    /// Выдать документ контрагенту.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="document">Документ.</param>
    /// <param name="days">Дней до планируемого возврата.</param>
    /// <param name="performerId">Id ответственного.</param>
    /// <param name="action">Действие (отправка контрагенту или согласование с контрагентом).</param>
    /// <remarks>Если не указать количество дней (null или 0), срок возврата указан не будет.</remarks>
    public static void IssueDocumentToCounterparty(IApprovalTask task, IOfficialDocument document, int? days, int performerId, Enumeration action)
    {
      var daysHasValue = days.HasValue && days != 0;
      Logger.DebugFormat("IssueDocumentToCounterparty: Task {0} with document {1} must be issued to {2} days with action = {3}",
                         task == null ? 0 : task.Id, document.Id, daysHasValue ? days.Value : 0, action.Value);
      var issue = document.Tracking.AddNew();
      var performer = Sungero.Company.Employees.Get(performerId);
      issue.DeliveredTo = performer;
      issue.Action = action;
      issue.DeliveryDate = Calendar.GetUserToday(performer);
      issue.IsOriginal = true;
      issue.ReturnTask = task;
      issue.Note = daysHasValue ? ApprovalTasks.Resources.CommentOnEndorsement : ApprovalTasks.Resources.CommentSigned;
      if (daysHasValue)
        issue.ReturnDeadline = Calendar.Now.AddWorkingDays(performer, days.Value).ToUserTime(performer).Date;
      else
        issue.ReturnDeadline = null;
    }

    #endregion
    
    /// <summary>
    /// Проверка, что проверяемый этап идет до определенного этапа в регламенте.
    /// </summary>
    /// <param name="firstStageType">Проверяемый этап.</param>
    /// <param name="secondStageType">Этап, который должен идти после проверяемого.</param>
    /// /// <param name="allowAdditionalApprovers">Признак этапа с дополнительными согласующими.</param>
    /// <returns>True, если проверяемый этап идет до определенного этапа в регламенте.</returns>
    [Remote(IsPure = true)]
    public bool CheckSequenceOfCoupleStages(Enumeration firstStageType, Enumeration secondStageType, bool allowAdditionalApprovers)
    {
      var stagesSequence = Functions.ApprovalTask.GetStages(_obj);
      
      var lastStageWithFirstType = stagesSequence.Stages.Where(st => st.Stage.StageType == firstStageType).LastOrDefault();
      var lastStageWithSecondType = stagesSequence.Stages.Where(st => st.Stage.StageType == secondStageType && st.Stage.AllowAdditionalApprovers == allowAdditionalApprovers).LastOrDefault();
      
      if (lastStageWithFirstType == null)
        return false;
      
      if (lastStageWithSecondType == null)
        return false;
      
      var lastStageWithFirstTypeNumber = stagesSequence.Stages.IndexOf(lastStageWithFirstType);
      var lastStageWithSecondTypeNumber = stagesSequence.Stages.IndexOf(lastStageWithSecondType);
      
      return lastStageWithFirstTypeNumber < lastStageWithSecondTypeNumber;
    }
    
    /// <summary>
    /// Проверка наличия прав на подпись документов во вложении у сотрудника, выбранного в качестве подписывающего.
    /// </summary>
    /// <param name="signatory">Подписывающий.</param>
    /// <param name="stages">Этапы согласования в правильном порядке.</param>
    /// <returns>True - если выбранный подписывающий имеет право подписи документа или
    /// в случае, если поле "На подпись" не заполнено (для обычной валидации).
    /// False - если у выбранного сотрудника нет права подписи.
    /// </returns>
    [Remote(IsPure = true)]
    public bool CheckSignatory(IEmployee signatory,  System.Collections.Generic.List<Structures.Module.DefinedApprovalStageLite> stages)
    {
      var document = _obj.DocumentGroup.OfficialDocuments.FirstOrDefault();
      if (document == null)
        return false;

      var hasSignStage = Functions.ApprovalRuleBase.HasApprovalStage(_obj.ApprovalRule, Docflow.ApprovalStage.StageType.Sign, document, stages);
      if (!hasSignStage)
        return true;
      
      var signatories = Functions.OfficialDocument.GetSignatories(document);
      if (signatory != null)
        return signatories.Any(s => Equals(s.EmployeeId, signatory.Id));
      else
        return false;
    }
    
    /// <summary>
    /// Получить последнее задание по задаче.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="assignmentCreated">Дата создания задания на доработку.</param>
    /// <returns>True, если подписание завершилось с результатом "Отказать".</returns>
    public static IAssignment GetLastTaskAssigment(ITask task, DateTime? assignmentCreated)
    {
      var taskAssignments = Assignments.GetAll().Where(o => Equals(o.Task, task));

      if (assignmentCreated.HasValue)
        taskAssignments = taskAssignments.Where(o => o.Created < assignmentCreated);
      
      var lastAssignment = taskAssignments.OrderByDescending(o => o.Created).FirstOrDefault();
      
      return lastAssignment;
    }
    
    /// <summary>
    /// Проверка, не отказал ли подписывающий.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="assignmentCreated">Дата создания задания на доработку.</param>
    /// <returns>True, если подписание завершилось с результатом "Отказать".</returns>
    public static bool IsSignatoryAbortTask(ITask task, DateTime? assignmentCreated)
    {
      var lastAssignment = GetLastTaskAssigment(task, assignmentCreated);
      if (lastAssignment == null || !ApprovalSigningAssignments.Is(lastAssignment))
        return false;
      
      return lastAssignment.Result == Docflow.ApprovalSigningAssignment.Result.Abort;
    }
    
    /// <summary>
    /// Проверка, не отказал ли КА в подписании.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="assignmentCreated">Дата создания задания на доработку.</param>
    /// <returns>True, если подписание завершилось с результатом "Отказать".</returns>
    public static bool IsExternalSignatoryAbortTask(ITask task, DateTime? assignmentCreated)
    {
      var lastAssignment = GetLastTaskAssigment(task, assignmentCreated);
      if (lastAssignment == null || !ApprovalCheckReturnAssignments.Is(lastAssignment))
        return false;
      
      return lastAssignment.Result == Docflow.ApprovalCheckReturnAssignment.Result.NotSigned;
    }
    
    /// <summary>
    /// Проверка, запрошено ли УОУ контрагентом.
    /// </summary>
    /// <param name="document">Документ для согласования.</param>
    /// <returns>True, если пришло УОУ.</returns>
    public static bool IsInvoiceAmendmentRequest(IOfficialDocument document)
    {
      var documentInfo = Sungero.Exchange.PublicFunctions.ExchangeDocumentInfo.Remote.GetLastDocumentInfo(document);
      if (documentInfo == null)
        return false;
      
      var serviceDocument = documentInfo.ServiceDocuments
        .Where(x => x.Date != null && (x.DocumentType == ExchDocumentType.Reject || x.DocumentType == ExchDocumentType.IReject))
        .OrderByDescending(x => x.Date)
        .FirstOrDefault();
      return serviceDocument.DocumentType == ExchDocumentType.IReject;
    }
    
    /// <summary>
    /// Проверка, не отказал ли рассматривающий.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="assignmentCreated">Дата создания задания на доработку.</param>
    /// <returns>True, если рассмотрение завершилось с результатом "Отказать".</returns>
    public static bool IsAddresseeAbortTask(ITask task, DateTime? assignmentCreated)
    {
      var lastAssignment = GetLastTaskAssigment(task, assignmentCreated);
      if (lastAssignment == null || !ApprovalReviewAssignments.Is(lastAssignment))
        return false;
      
      return lastAssignment == null ? false : lastAssignment.Result == Docflow.ApprovalReviewAssignment.Result.Abort;
    }
    
    /// <summary>
    /// Проверка, что статус вложения Отозван.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <returns>True, если документ во вложении со статусом "Отозван".</returns>
    public static bool IsAttachmentObsolete(ITask task)
    {
      var lastAssignment = GetLastTaskAssigment(task, null);
      var isAttachmentObsolete = lastAssignment.AllAttachments.Where(x => OfficialDocuments.Is(x) && OfficialDocuments.As(x).ExchangeState == Docflow.OfficialDocument.ExchangeState.Obsolete).Any();
      return isAttachmentObsolete;
    }
    
    /// <summary>
    /// Получить признак наличия согласования автором задачи или исполнителем задания доработки.
    /// </summary>
    /// <param name="assignee">Автор задачи или исполнитель задания доработки.</param>
    /// <param name="approvers">Список согласующих, в который может попасть инициатор.</param>
    /// <returns>Признак согласования инициатором и признак необходимости усиленной подписи.</returns>
    [Remote(IsPure = true)]
    public Structures.ApprovalTask.ApprovalStatus AuthorMustApproveDocument(IUser assignee, List<IRecipient> approvers)
    {
      var stages = Functions.ApprovalTask.GetStages(_obj).Stages;
      var approvalStages = stages.Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Approvers);
      var managerStage = stages.FirstOrDefault(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Manager);
      
      var approvalWithAssignee = approvalStages
        .Where(s => Functions.ApprovalStage.GetStagePerformers(_obj, s.Stage, approvers).Contains(assignee))
        .ToList();
      if (managerStage != null && Equals(Functions.ApprovalStage.GetStagePerformer(_obj, managerStage.Stage), assignee))
        approvalWithAssignee.Add(managerStage);
      
      return Structures.ApprovalTask.ApprovalStatus
        .Create(approvalWithAssignee.Any(), approvalWithAssignee.Any(a => a.Stage.NeedStrongSign == true));
    }
    
    /// <summary>
    /// Проверить, согласован ли пользователем документ в рамках последней итерации согласования.
    /// </summary>
    /// <param name="user">Пользователь, чья подпись проверяется.</param>
    /// <returns>True, если имеется согласующая валидная подпись.</returns>
    public bool HasValidSignature(IUser user)
    {
      // Определить дату старта новой итерации.
      var lastRework = ApprovalReworkAssignments
        .GetAll(a => Equals(a.MainTask, _obj) && a.Status == Workflow.AssignmentBase.Status.Completed)
        .OrderByDescending(a => a.Created)
        .FirstOrDefault();
      if (lastRework != null)
        Logger.DebugFormat("Find last rework assignment id {0}.", lastRework.Id);
      
      var lastIterationDate = (lastRework != null && lastRework.Created > _obj.Started) ? lastRework.Created : _obj.Started;
      Logger.DebugFormat("Find last iteration date {0}.", lastIterationDate);
      
      // Найти исполнителей заданий на согласование.
      var approvalAssignments = Assignments
        .GetAll(a => Equals(a.MainTask, _obj) &&
                a.Created >= lastIterationDate &&
                (ApprovalAssignments.Is(a) ||
                 ApprovalManagerAssignments.Is(a) ||
                 ApprovalSigningAssignments.Is(a) ||
                 ApprovalReworkAssignments.Is(a)));
      var approvers = approvalAssignments
        .Select(a => a.CompletedBy)
        .ToList();
      approvers.AddRange(approvalAssignments.Select(a => a.Performer));
      if (lastRework == null || Equals(lastRework.Performer, _obj.Author))
        approvers.Add(_obj.Author);
      
      // Если проверяемый пользователь не выполнял задания согласования,
      // то считаем, что он не мог согласовать документ в рамках процесса.
      if (!approvers.Contains(user))
      {
        Logger.DebugFormat("No assignment for user Id {0}.", user.Id);
        return false;
      }

      var isReworkPerformer = lastRework != null && Equals(lastRework.CompletedBy, user);
      var document = _obj.DocumentGroup.OfficialDocuments.FirstOrDefault();
      if (document == null || document.LastVersion == null)
      {
        if (Equals(_obj.StartedBy, user) && lastRework == null || isReworkPerformer)
          return true;
        
        var approvalAssignment = approvalAssignments.Where(a => Equals(a.CompletedBy, user)).OrderByDescending(i => i.Modified).FirstOrDefault();
        if (approvalAssignment != null)
          Logger.DebugFormat("Find approval assignment id {0} with approved {1}.", approvalAssignment.Id, approvalAssignment.Result == Docflow.ApprovalAssignment.Result.Approved);
        
        return approvalAssignment == null ? false : approvalAssignment.Result == Docflow.ApprovalAssignment.Result.Approved;
      }
      
      var hasUserSignature = Signatures.Get(document.LastVersion)
        .Any(s => Equals(s.SubstitutedUser ?? s.Signatory, user) && s.SignatureType != SignatureType.NotEndorsing && s.IsValid);
      return hasUserSignature;
    }
    
    /// <summary>
    /// Обновить статус согласования документа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="state">Новый статус.</param>
    public static void UpdateApprovalState(IApprovalTask task, Enumeration? state)
    {
      var document = task.DocumentGroup.OfficialDocuments.FirstOrDefault();
      var currentState = document.InternalApprovalState;

      // Не меняем статус подписанного документа.
      if (currentState == InternalApprovalState.Signed)
      {
        Logger.DebugFormat("UpdateApprovalState: Task {0}, document {1} already signed.", task.Id, document.Id);
        return;
      }
      
      // Не меняем статус рассмотренного документа.
      if (currentState == Docflow.Memo.InternalApprovalState.Reviewed)
      {
        Logger.DebugFormat("UpdateApprovalState: Task {0}, document {1} already reviewed.", task.Id, document.Id);
        return;
      }
      
      Logger.DebugFormat("UpdateApprovalState: Task {3}, document {0}, {1} -> {2}", document.Id, currentState, state, task.Id);
      
      if (document.InternalApprovalState != state)
        document.InternalApprovalState = state;
    }

    /// <summary>
    /// Обработчик изменения правила согласования.
    /// </summary>
    /// <param name="rule">Новое правило.</param>
    /// <param name="stages">Список этапов согласования.</param>
    [Remote(PackResultEntityEagerly = true)]
    public virtual void ApprovalRuleChanged(IApprovalRuleBase rule, List<Structures.Module.DefinedApprovalStageLite> stages)
    {
      this.UpdateReglamentApprovers(rule, stages);
      _obj.AddApprovers.Clear();
      _obj.AddApproversExpanded.Clear();
      _obj.Signatory = null;
      _obj.Addressee = null;
      
      if (rule != null)
      {
        var document = _obj.DocumentGroup.OfficialDocuments.FirstOrDefault();
        
        var hasConditionWithSignatoryRole = false;
        var hasConditionWithAddresseeRole = false;
        var hasConditionWithSignAssistantRole = false;
        var hasConditionWithAddrAssistantRole = false;
        var hasConditionWithPrintRespRole = false;
        
        if (document != null)
        {
          // Список достижимых условий в правиле согласования.
          var conditions = Functions.ApprovalRuleBase.GetConditions(rule, document, _obj);
          
          hasConditionWithSignatoryRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(rule, conditions, Docflow.ApprovalRoleBase.Type.Signatory);
          hasConditionWithAddresseeRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(rule, conditions, Docflow.ApprovalRoleBase.Type.Addressee);
          hasConditionWithSignAssistantRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(rule, conditions, Docflow.ApprovalRoleBase.Type.SignAssistant);
          hasConditionWithAddrAssistantRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(rule, conditions, Docflow.ApprovalRoleBase.Type.AddrAssistant);
          hasConditionWithPrintRespRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(rule, conditions, Docflow.ApprovalRoleBase.Type.PrintResp);
        }
        
        var signingStage = stages.Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Sign).FirstOrDefault();
        if (signingStage != null || hasConditionWithSignatoryRole || hasConditionWithSignAssistantRole || hasConditionWithPrintRespRole)
          _obj.Signatory = Functions.Module.GetPerformerSignatory(_obj);
        
        var reviewStage = stages.Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Review).FirstOrDefault();
        if (reviewStage != null || hasConditionWithAddresseeRole || hasConditionWithAddrAssistantRole)
        {
          // Заполнить адресата из документа.
          var memo = Memos.As(_obj.DocumentGroup.OfficialDocuments.FirstOrDefault());
          if (memo != null)
            _obj.Addressee = memo.Addressee;
        }
        
        // Заполнить адресата из этапа рассмотрения, если указан выделенный сотрудник.
        if (reviewStage != null &&
            reviewStage.Stage.AssigneeType == Sungero.Docflow.ApprovalStage.AssigneeType.Employee &&
            reviewStage.Stage.Assignee != null &&
            Company.Employees.Is(reviewStage.Stage.Assignee))
          _obj.Addressee = Company.Employees.As(reviewStage.Stage.Assignee);
        
        var hasSendStage = Functions.ApprovalRuleBase.HasApprovalStage(_obj.ApprovalRule, Docflow.ApprovalStage.StageType.Sending, document, stages) ||
          Functions.ApprovalRuleBase.HasApprovalCondition(_obj.ApprovalRule, document, _obj, Docflow.ConditionBase.ConditionType.DeliveryMethod);
        if (hasSendStage == true)
        {
          _obj.ExchangeService = Functions.ApprovalTask.GetExchangeServices(_obj).DefaultService;
          var outgoingDocument = OutgoingDocumentBases.As(document);
          if (_obj.ExchangeService != null)
            _obj.DeliveryMethod = Functions.MailDeliveryMethod.GetExchangeDeliveryMethod();
          else if (outgoingDocument != null && outgoingDocument.IsManyAddressees != true)
            _obj.DeliveryMethod = document.DeliveryMethod;
        }
        else
        {
          _obj.DeliveryMethod = null;
          _obj.ExchangeService = null;
        }
      }
    }
    
    /// <summary>
    /// Обработчик изменения правила согласования.
    /// </summary>
    /// <param name="rule">Новое правило.</param>
    [Remote(PackResultEntityEagerly = true)]
    public virtual void ApprovalRuleChanged(IApprovalRuleBase rule)
    {
      var stages = Functions.ApprovalTask.GetStages(_obj).Stages;
      this.ApprovalRuleChanged(rule, stages);
    }
    
    /// <summary>
    /// Обновить список обязательных согласующих.
    /// </summary>
    /// <param name="rule">Правило.</param>
    /// <param name="stages">Список этапов согласования.</param>
    [Remote(PackResultEntityEagerly = true)]
    public void UpdateReglamentApprovers(IApprovalRuleBase rule, List<Structures.Module.DefinedApprovalStageLite> stages)
    {
      _obj.ReqApprovers.Clear();
      
      if (rule == null)
        return;

      // Включить руководителя в список обязательных согласующих.
      var managerStage = stages.Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Manager).FirstOrDefault();
      if (managerStage != null)
      {
        var manager = Functions.ApprovalStage.GetRemoteStagePerformer(_obj, managerStage.Stage);
        if (manager != null && !manager.Equals(_obj.Author))
          _obj.ReqApprovers.AddNew().Approver = manager;
      }
      
      foreach (var approver in Functions.ApprovalTask.GetAllRequiredApprovers(_obj, stages))
      {
        _obj.ReqApprovers.AddNew().Approver = approver;
      }
    }
    
    /// <summary>
    /// Обновить список обязательных согласующих.
    /// </summary>
    /// <param name="rule">Правило.</param>
    [Remote(PackResultEntityEagerly = true)]
    public void UpdateReglamentApprovers(IApprovalRuleBase rule)
    {
      var stages = Functions.ApprovalTask.GetStages(_obj).Stages;
      this.UpdateReglamentApprovers(rule, stages);
    }

    /// <summary>
    /// Получить всех обязательных сотрудников процесса согласования.
    /// </summary>
    /// <returns>Список обязательных сотрудников.</returns>
    public List<IEmployee> GetAllRequiredApprovers()
    {
      var stages = Functions.ApprovalTask.GetStages(_obj).Stages;
      return this.GetAllRequiredApprovers(stages);
    }
    
    /// <summary>
    /// Получить всех обязательных сотрудников процесса согласования.
    /// </summary>
    /// <param name="stages">Список этапов согласования.</param>
    /// <returns>Обязательные сотрудники.</returns>
    public List<IEmployee> GetAllRequiredApprovers(List<Structures.Module.DefinedApprovalStageLite> stages)
    {
      var approversStages = stages
        .Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Approvers)
        .Select(s => s.Stage).ToList();
      
      var recipients = new List<IRecipient>();
      foreach (var stage in approversStages)
      {
        // Сотрудники/группы.
        if (stage.Recipients.Any())
          recipients.AddRange(stage.Recipients
                              .Where(rec => rec.Recipient != null)
                              .Select(rec => rec.Recipient)
                              .ToList());
        
        // Роли согласования.
        if (stage.ApprovalRoles.Any())
          recipients.AddRange(stage.ApprovalRoles
                              .Where(r => r.ApprovalRole != null && r.ApprovalRole.Type != Docflow.ApprovalRoleBase.Type.Approvers)
                              .Select(r => Functions.ApprovalRoleBase.GetRolePerformer(r.ApprovalRole, _obj))
                              .Where(r => r != null)
                              .ToList());
      }

      var performers = Docflow.Functions.Module.GetEmployeesFromRecipients(recipients).Distinct().ToList();
      var assignments = ApprovalAssignments.GetAll()
        .Where(a => Equals(a.Task, _obj) && Equals(a.TaskStartId, _obj.StartId))
        .ToList();
      
      // Поиск обязательных.
      foreach (var assignment in assignments)
      {
        if (!_obj.AddApproversExpanded.Any(x => Equals(x.Approver, assignment.Performer)))
        {
          performers.Add(Employees.As(assignment.Performer));
          performers = performers.Distinct().ToList();
        }
      }

      var start = assignments.Count + 1;
      while (start > assignments.Count)
      {
        start = assignments.Count;
        var delete = new List<IAssignment>();
        foreach (var assignment in assignments)
        {
          if (assignment.ForwardedTo == null)
            continue;
          
          if (performers.Contains(assignment.Performer))
          {
            performers.AddRange(assignment.ForwardedTo.Select(u => Employees.As(u)));
            performers = performers.Distinct().ToList();
            delete.Add(assignment);
          }
        }
        assignments.RemoveAll(a => delete.Contains(a));
      }

      return performers;
    }
    
    /// <summary>
    /// Определить текущий этап.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stageType">Тип этапа.</param>
    /// <returns>Текущий этап, либо null, если этапа нет (или это не тот этап).</returns>
    public static Structures.Module.DefinedApprovalStageLite GetStage(IApprovalTask task, Enumeration stageType)
    {
      var stage = task.ApprovalRule.Stages
        .Where(s => s.Stage.StageType == stageType)
        .FirstOrDefault(s => s.Number == task.StageNumber);
      
      if (stage != null)
        return Structures.Module.DefinedApprovalStageLite.Create(stage.Stage, stage.Number, stage.StageType);
      
      return null;
    }
    
    /// <summary>
    /// Определить, необходим ли контроль возврата.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>True, если необходим контроль возврата.</returns>
    public static bool NeedControlReturn(IApprovalTask task)
    {
      var document = task.DocumentGroup.OfficialDocuments.First();
      
      // Если КА в МКДО ожидает подписи от нас или уже подписан двумя сторонами, то контроль возврата не нужен.
      if (document.ExchangeState == Docflow.OfficialDocument.ExchangeState.SignRequired ||
          document.ExchangeState == Docflow.OfficialDocument.ExchangeState.Signed)
        return false;
      
      // Если КА подписал, то контроля возврата не нужен.
      return document.ExternalApprovalState != Docflow.OfficialDocument.ExternalApprovalState.Signed;
    }
    
    /// <summary>
    /// Получить локализованное имя результата согласования по подписи.
    /// </summary>
    /// <param name="signature">Подпись.</param>
    /// <param name="emptyIfNotValid">Вернуть пустую строку, если подпись не валидна.</param>
    /// <returns>Локализованный результат подписания.</returns>
    public static string GetEndorsingResultFromSignature(Sungero.Domain.Shared.ISignature signature, bool emptyIfNotValid)
    {
      var result = string.Empty;
      
      if (emptyIfNotValid && !signature.IsValid)
        return result;
      
      switch (signature.SignatureType)
      {
        case SignatureType.Approval:
          result = ApprovalTasks.Resources.ApprovalFormApproved;
          break;
        case SignatureType.Endorsing:
          result = ApprovalTasks.Resources.ApprovalFormEndorsed;
          break;
        case SignatureType.NotEndorsing:
          result = ApprovalTasks.Resources.ApprovalFormNotEndorsed;
          break;
      }
      
      return result;
    }
    
    /// <summary>
    /// Заполнить SQL таблицу для формирования отчета "Лист согласования".
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="reportSessionId">Идентификатор отчета.</param>
    public static void UpdateApprovalSheetReportTable(IOfficialDocument document, string reportSessionId)
    {
      var filteredSignatures = new Dictionary<string, ISignature>();
      
      var setting = Docflow.PublicFunctions.PersonalSetting.GetPersonalSettings(null);
      var showNotApproveSign = setting != null ? setting.ShowNotApproveSign == true : false;
      
      foreach (var version in document.Versions.OrderByDescending(v => v.Created))
      {
        var versionSignatures = Signatures.Get(version).Where(s => (showNotApproveSign || s.SignatureType != SignatureType.NotEndorsing)
                                                              && s.IsExternal != true
                                                              && !filteredSignatures.ContainsKey(GetSignatureKey(s, version.Number.Value)));
        var lastSignaturesInVersion = versionSignatures
          .GroupBy(v => GetSignatureKey(v, version.Number.Value))
          .Select(grouping => grouping.Where(s => s.SigningDate == grouping.Max(last => last.SigningDate)).First());
        
        foreach (ISignature signature in lastSignaturesInVersion)
        {
          filteredSignatures.Add(GetSignatureKey(signature, version.Number.Value), signature);
          if (!signature.IsValid)
            foreach (var error in signature.ValidationErrors)
              Logger.DebugFormat("UpdateApprovalSheetReportTable: reportSessionId {0}, document {1}, version {2}, signatory {3}, substituted user {7}, signature {4}, with error {5} - {6}",
                                 reportSessionId, document.Id, version.Number,
                                 signature.Signatory != null ? signature.Signatory.Name : signature.SignatoryFullName, signature.Id, error.ErrorType, error.Message,
                                 signature.SubstitutedUser != null ? signature.SubstitutedUser.Name : string.Empty);
          
          // Dmitriev_IA: signature.AdditionalInfo формируется в Employee в действии "Получение информации о подписавшем".
          //              Может содержать лишние пробелы в должности сотрудника. US 89747.
          var employeeName = string.Empty;
          var additionalInfos = (signature.AdditionalInfo ?? string.Empty)
            .Split(new char[] { '|' }, StringSplitOptions.None)
            .Select(x => x.Trim())
            .ToList();
          if (signature.SubstitutedUser == null)
          {
            var additionalInfo = additionalInfos.FirstOrDefault();
            employeeName = string.Format("<b>{1}</b>{0}", signature.SignatoryFullName, AddEndOfLine(additionalInfo)).Trim();
          }
          else
          {
            if (additionalInfos.Count() == 3)
            {
              // Замещающий.
              var signatoryAdditionalInfo = additionalInfos[0];
              if (!string.IsNullOrEmpty(signatoryAdditionalInfo))
                signatoryAdditionalInfo = AddEndOfLine(string.Format("<b>{0}</b>", signatoryAdditionalInfo));
              var signatoryText = AddEndOfLine(string.Format("{0}{1}", signatoryAdditionalInfo, signature.SignatoryFullName));
              
              // Замещаемый.
              var substitutedUserAdditionalInfo = additionalInfos[1];
              if (!string.IsNullOrEmpty(substitutedUserAdditionalInfo))
                substitutedUserAdditionalInfo = AddEndOfLine(string.Format("<b>{0}</b>", substitutedUserAdditionalInfo));
              var substitutedUserText = string.Format("{0}{1}", substitutedUserAdditionalInfo, signature.SubstitutedUserFullName);
              
              // Замещающий за замещаемого.
              var onBehalfOfText = AddEndOfLine(ApprovalTasks.Resources.OnBehalfOf);
              employeeName = string.Format("{0}{1}{2}", signatoryText, onBehalfOfText, substitutedUserText);
            }
            else if (additionalInfos.Count() == 2)
            {
              // Замещающий / Система.
              var signatoryText = AddEndOfLine(signature.SignatoryFullName);
              
              // Замещаемый.
              var substitutedUserAdditionalInfo = additionalInfos[0];
              if (!string.IsNullOrEmpty(substitutedUserAdditionalInfo))
                substitutedUserAdditionalInfo = AddEndOfLine(string.Format("<b>{0}</b>", substitutedUserAdditionalInfo));
              var substitutedUserText = string.Format("{0}{1}", substitutedUserAdditionalInfo, signature.SubstitutedUserFullName);
              
              // Система за замещаемого.
              var onBehalfOfText = AddEndOfLine(ApprovalTasks.Resources.OnBehalfOf);
              employeeName = string.Format("{0}{1}{2}", signatoryText, onBehalfOfText, substitutedUserText);
            }
          }
          
          var commandText = Queries.ApprovalSheetReport.InsertIntoApprovalSheetReportTable;
          
          using (var command = SQL.GetCurrentConnection().CreateCommand())
          {
            var separator = ", ";
            var errorString = Docflow.PublicFunctions.Module.GetSignatureValidationErrorsAsString(signature, separator);
            var signErrors = string.IsNullOrWhiteSpace(errorString)
              ? Reports.Resources.ApprovalSheetReport.SignStatusActive
              : Docflow.PublicFunctions.Module.ReplaceFirstSymbolToUpperCase(errorString.ToLower());
            var resultString = Functions.ApprovalTask.GetEndorsingResultFromSignature(signature, false);
            command.CommandText = commandText;
            SQL.AddParameter(command, "@reportSessionId",  reportSessionId, System.Data.DbType.String);
            SQL.AddParameter(command, "@employeeName",  employeeName, System.Data.DbType.String);
            SQL.AddParameter(command, "@resultString",  resultString, System.Data.DbType.String);
            SQL.AddParameter(command, "@comment",  signature.Comment, System.Data.DbType.String);
            SQL.AddParameter(command, "@signErrors",  signErrors, System.Data.DbType.String);
            SQL.AddParameter(command, "@versionNumber",  version.Number, System.Data.DbType.Int32);
            SQL.AddParameter(command, "@SignatureDate",  signature.SigningDate.FromUtcTime(), System.Data.DbType.DateTime);
            
            command.ExecuteNonQuery();
          }
        }
      }
    }

    /// <summary>
    /// Получить ключ для подписи.
    /// </summary>
    /// <param name="signature">Подпись.</param>
    /// <param name="versionNumber">Номер версии.</param>
    /// <returns>Ключ для подписи.</returns>
    private static string GetSignatureKey(ISignature signature, int versionNumber)
    {
      // Если подпись не "несогласующая", она должна схлапываться вне версий.
      if (signature.SignatureType != SignatureType.NotEndorsing)
        versionNumber = 0;
      
      if (signature.Signatory != null)
      {
        if (signature.SubstitutedUser != null && !signature.SubstitutedUser.Equals(signature.Signatory))
          return string.Format("{0} - {1}:{2}:{3}", signature.Signatory.Id, signature.SubstitutedUser.Id, signature.SignatureType == SignatureType.Approval, versionNumber);
        else
          return string.Format("{0}:{1}:{2}", signature.Signatory.Id, signature.SignatureType == SignatureType.Approval, versionNumber);
      }
      else
        return string.Format("{0}:{1}:{2}", signature.SignatoryFullName, signature.SignatureType == SignatureType.Approval, versionNumber);
    }

    /// <summary>
    /// Добавить перенос в конец строки, если она не пуста.
    /// </summary>
    /// <param name="row">Строка.</param>
    /// <returns>Результирующая строка.</returns>
    private static string AddEndOfLine(string row)
    {
      return string.IsNullOrWhiteSpace(row) ? string.Empty : row + Environment.NewLine;
    }

    /// <summary>
    /// Выдать права на вложения, не выше прав инициатора задачи.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="performers">Исполнители.</param>
    public static void GrantRightForAttachmentsToPerformers(IApprovalTask task, List<IRecipient> performers)
    {
      foreach (var performer in performers)
      {
        // На основной документ - на изменение.
        var approvalDocument = task.DocumentGroup.OfficialDocuments.First();
        if (!approvalDocument.AccessRights.IsGrantedDirectly(DefaultAccessRightsTypes.FullAccess, performer))
          approvalDocument.AccessRights.Grant(performer, DefaultAccessRightsTypes.Change);
        
        // На приложения - на изменение, но не выше, чем у инициатора.
        foreach (var document in task.AddendaGroup.OfficialDocuments)
        {
          if (document.AccessRights.IsGrantedDirectly(DefaultAccessRightsTypes.FullAccess, performer))
            continue;

          var rightType = document.AccessRights.CanUpdate(task.Author) ? DefaultAccessRightsTypes.Change : DefaultAccessRightsTypes.Read;
          document.AccessRights.Grant(performer, rightType);
        }
      }
    }

    /// <summary>
    /// Получить плановых исполнителей.
    /// </summary>
    /// <returns>Исполнители.</returns>
    public virtual List<IRecipient> GetTaskAdditionalAssignees()
    {
      return this.GetTaskAssignees(true);
    }
    
    /// <summary>
    /// Получить плановых исполнителей.
    /// </summary>
    /// <param name="withObservers">Включать в результат наблюдателей.</param>
    /// <returns>Исполнители.</returns>
    public virtual List<IRecipient> GetTaskAssignees(bool withObservers)
    {
      var assignees = new List<IRecipient>();

      var approvalTask = ApprovalTasks.As(_obj);
      if (approvalTask == null)
        return assignees;

      var stages = Functions.ApprovalTask.GetStages(approvalTask).Stages.Where(s => s.Stage != null).Select(s => s.Stage);
      foreach (var stage in stages)
      {
        var stageType = stage.StageType;
        
        // Задания с одним исполнителем.
        if (stageType == StageType.Manager || stageType == StageType.Print || stageType == StageType.Sign ||
            stageType == StageType.Register || stageType == StageType.Sending ||
            stageType == StageType.Execution || stageType == StageType.Review)
        {
          var assignee = Functions.ApprovalStage.GetStagePerformer(approvalTask, stage);
          if (assignee != null)
            assignees.Add(assignee);
        }
        
        // Задания с несколькими исполнителями.
        if (stageType == StageType.Approvers ||
            stageType == StageType.CheckReturn)
        {
          var stageAssignees = Functions.ApprovalStage.GetStagePerformers(approvalTask, stage);
          if (stageAssignees.Any())
            assignees.AddRange(stageAssignees);
        }
        
        // Для заданий и уведомлений права выдать на группы/роли, а не конкретным исполнителям.
        if (stageType == StageType.SimpleAgr || stageType == StageType.Notice)
        {
          var stageAssignees = Functions.ApprovalStage.GetStageRecipients(stage, approvalTask);
          if (stageAssignees.Any())
            assignees.AddRange(stageAssignees);
        }
      }
      
      if (withObservers)
        assignees.AddRange(approvalTask.Observers.Where(a => a.Observer != null).Select(a => a.Observer));
      
      return assignees.Distinct().ToList();
    }

    /// <summary>
    /// Получение последнего задания на доработку.
    /// </summary>
    /// <returns>Последнее задание на доработку.</returns>
    public IApprovalReworkAssignment GetLastReworkAssignment()
    {
      return ApprovalReworkAssignments
        .GetAll(a => Equals(a.Task, _obj) && a.Created > _obj.Started)
        .OrderByDescending(asg => asg.Created)
        .FirstOrDefault();
    }

    /// <summary>
    /// Заполнить список согласующих в задании доп.согласующих.
    /// </summary>
    /// <param name="block">Блок доработки.</param>
    /// <param name="approvers">Список согласующих.</param>
    /// <param name="isRequiredApprovers">Признак, обязательные согласующие или нет.</param>
    public void FillApproversList(Sungero.Docflow.Server.ApprovalReworkAssignmentBlock block,
                                  List<IEmployee> approvers,
                                  bool isRequiredApprovers)
    {
      var approvalAssignments = ApprovalAssignments.GetAll(a => Equals(a.Task, _obj) && a.Created >= _obj.Started).ToList();
      var lastReworkAssignment = Functions.ApprovalTask.GetLastReworkAssignment(_obj);
      
      // Обновить список согласующих.
      foreach (var approver in approvers)
      {
        if (block.Approvers != null && block.Approvers.Any(a => Equals(a.Approver, approver)))
          continue;
        
        var newApprover = block.Approvers.AddNew();
        newApprover.Approver = approver;
        newApprover.IsRequiredApprover = isRequiredApprovers;
        
        // Согласовал или не согласовал. Если задания не было, то не согласовали.
        var approvalAssignment = approvalAssignments
          .Where(a => Equals(a.Performer, approver))
          .OrderByDescending(i => i.Modified)
          .FirstOrDefault();
        var forwarded = approvalAssignment != null && approvalAssignment.Result == Sungero.Docflow.ApprovalAssignment.Result.Forward;
        var approved = approvalAssignment != null && approvalAssignment.Result == Sungero.Docflow.ApprovalAssignment.Result.Approved;
        if (approved)
          newApprover.Approved = Docflow.ApprovalReworkAssignmentApprovers.Approved.IsApproved;
        else if (forwarded)
          newApprover.Approved = Docflow.ApprovalReworkAssignmentApprovers.Approved.Forwarded;
        else
          newApprover.Approved = Docflow.ApprovalReworkAssignmentApprovers.Approved.NotApproved;
        
        // Было ли задание с момента последней доработки.
        var hasAssignmentAfterLastRework = approvalAssignment != null &&
          (lastReworkAssignment == null || approvalAssignment.Created >= lastReworkAssignment.Created);
        
        // Если уменьшающийся круг запрещен, то действие может быть только - Отправить на согласование.
        if (_obj.ApprovalRule.IsSmallApprovalAllowed != true)
        {
          // Исключая переадресацию.
          if (forwarded)
            newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.DoNotSend;
          else
            newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.SendForApproval;
          continue;
        }
        
        // Предыдущее действие, выбранное на доработке.
        Enumeration? lastApproverAction = null;
        if (lastReworkAssignment != null)
        {
          var lastReworkAssignmentGridApprover = lastReworkAssignment.Approvers.FirstOrDefault(app => Equals(app.Approver, approver));
          if (lastReworkAssignmentGridApprover != null)
            lastApproverAction = lastReworkAssignmentGridApprover.Action;
        }
        
        // Новое или повторное согласование (плюс переадресации -- когда в гриде было без отправки, а задание всё-таки есть).
        if (lastApproverAction == null || lastApproverAction == Docflow.ApprovalReworkAssignmentApprovers.Action.SendForApproval ||
            (lastApproverAction != Docflow.ApprovalReworkAssignmentApprovers.Action.SendForApproval && hasAssignmentAfterLastRework))
        {
          if (forwarded)
            newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.DoNotSend;
          else if (approved && hasAssignmentAfterLastRework)
            newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.SendNotice;
          else
            newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.SendForApproval;
          
          continue;
        }
        
        // В предыдущий раз не отправляли.
        if (lastApproverAction == Docflow.ApprovalReworkAssignmentApprovers.Action.DoNotSend)
        {
          newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.DoNotSend;
          continue;
        }
        
        // В предыдущий раз отправили уведомление.
        if (lastApproverAction == Docflow.ApprovalReworkAssignmentApprovers.Action.SendNotice)
        {
          var notice = ApprovalNotifications
            .GetAll(a => Equals(a.Task, _obj) && a.Created >= lastReworkAssignment.Created)
            .FirstOrDefault(a => Equals(a.Performer, approver));
          if (notice != null)
            newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.DoNotSend;
          else
            newApprover.Action = Docflow.ApprovalReworkAssignmentApprovers.Action.SendNotice;
          continue;
        }
      }
    }

    /// <summary>
    /// Получить всех сотрудников, которые участвовали в согласовании и подписании.
    /// </summary>
    /// <returns>Список пользователей.</returns>
    [Public]
    public List<IUser> GetAllApproversAndSignatories()
    {
      var approvalBlocks = new[] { "3", "6", "9" };
      return Assignments.GetAll()
        .Where(a => Equals(a.Task, _obj))
        .Where(a => a.Status == Sungero.Workflow.AssignmentBase.Status.Completed)
        .Where(a => approvalBlocks.Contains(a.BlockUid ?? "0"))
        .Select(a => a.Performer)
        .Distinct().ToList();
    }

    /// <summary>
    /// Получить сервисы обмена.
    /// </summary>
    /// <returns>Сервисы обмена.</returns>
    [Remote(IsPure = true)]
    public Structures.ApprovalTask.ExchangeServies GetExchangeServices()
    {
      var document = _obj.DocumentGroup.OfficialDocuments.FirstOrDefault();
      if (document != null)
      {
        if (Docflow.OutgoingDocumentBases.Is(document) && Docflow.OutgoingDocumentBases.As(document).IsManyAddressees == true)
          return Structures.ApprovalTask.ExchangeServies.Create(new List<ExchangeCore.IExchangeService>(), null);
        
        if (Docflow.PublicFunctions.OfficialDocument.Remote.CanSendAnswer(document))
        {
          var info = Exchange.PublicFunctions.ExchangeDocumentInfo.Remote.GetIncomingExDocumentInfo(document);
          if (info != null)
          {
            var service = ExchangeCore.PublicFunctions.BoxBase.GetExchangeService(info.Box);
            return Structures.ApprovalTask.ExchangeServies.Create(new List<ExchangeCore.IExchangeService>() { service }, service);
          }
        }
        
        // Если есть хоть один контрагент с МКДО, но нет контрагента без МКДО.
        var parties = Exchange.PublicFunctions.ExchangeDocumentInfo.GetDocumentCounterparties(document);
        if (parties != null && parties.Any(p => p.Status == CoreEntities.DatabookEntry.Status.Active))
        {
          if (Docflow.AccountingDocumentBases.Is(document) && Docflow.AccountingDocumentBases.As(document).IsFormalized == true)
          {
            var defaultService = Docflow.AccountingDocumentBases.As(document).BusinessUnitBox.ExchangeService;
            var services = new List<ExchangeCore.IExchangeService>() { defaultService };
            return Structures.ApprovalTask.ExchangeServies.Create(services, defaultService);
          }
          
          var lines = parties.SelectMany(p => p.ExchangeBoxes.Where(b => b.Status == Parties.CounterpartyExchangeBoxes.Status.Active &&
                                                                    Equals(b.Box.BusinessUnit, document.BusinessUnit))).ToList();
          var hasPartyWithoutActiveExchange = parties.Any(p => p.ExchangeBoxes
                                                          .Where(b => Equals(b.Box.BusinessUnit, document.BusinessUnit))
                                                          .All(b => b.Status != Parties.CounterpartyExchangeBoxes.Status.Active));
          if (lines.Any() && !hasPartyWithoutActiveExchange)
          {
            var services = lines
              .Select(l => l.Box.ExchangeService)
              .Distinct()
              .OrderByDescending(x => Equals(x.ExchangeProvider, ExchangeCore.ExchangeService.ExchangeProvider.Synerdocs))
              .ToList();
            return Structures.ApprovalTask.ExchangeServies.Create(services, services.FirstOrDefault());
          }
        }
        
        if (parties == null || !parties.Any())
        {
          var boxes = ExchangeCore.PublicFunctions.BusinessUnitBox.Remote.GetConnectedBoxes();
          
          var businessUnit = document.BusinessUnit;
          if (businessUnit == null)
            businessUnit = Company.PublicFunctions.BusinessUnit.Remote.GetBusinessUnit(Company.Employees.Current);
          var services = boxes.Where(b => Equals(b.BusinessUnit, businessUnit)).Select(x => x.ExchangeService).ToList()
            .Distinct()
            .OrderByDescending(x => Equals(x.ExchangeProvider, ExchangeCore.ExchangeService.ExchangeProvider.Synerdocs))
            .ToList();
          
          return Structures.ApprovalTask.ExchangeServies.Create(services, services.FirstOrDefault());
        }
      }
      return Structures.ApprovalTask.ExchangeServies.Create(new List<ExchangeCore.IExchangeService>(), null);
    }
    
    /// <summary>
    /// Помечает задачу для отправки на доработку, если не удалось вычислить исполнителя этапа.
    /// </summary>
    /// <param name="stage">Этап, исполнителя которого не удалось вычислить.</param>
    public void FillReworkReasonWhenAssigneeNotFound(IApprovalStage stage)
    {
      _obj.IsStageAssigneeNotFound = true;
      var hyperlink = Hyperlinks.Get(stage);
      _obj.ReworkReason = ApprovalTasks.Resources.ReworkReasonWhenAssigneeNotFoundFormat(hyperlink);
    }
    
    /// <summary>
    /// Выдать права на вложения автору задачи.
    /// </summary>
    [Remote]
    public void GrantRightsToAuthor()
    {
      // Права на основной документ.
      var document = _obj.DocumentGroup.OfficialDocuments.FirstOrDefault();
      document.AccessRights.Grant(_obj.Author, DefaultAccessRightsTypes.Change);
      document.Save();
      
      // Права на приложения.
      foreach (var doc in _obj.AddendaGroup.OfficialDocuments)
      {
        if (doc.AccessRights.IsGrantedDirectly(DefaultAccessRightsTypes.FullAccess, _obj.Author))
          continue;
        doc.AccessRights.Grant(_obj.Author, DefaultAccessRightsTypes.Change);
        doc.Save();
      }
    }
    
    /// <summary>
    /// Обновить способ доставки в задаче, документе, гриде адресатов исходящего письма.
    /// </summary>
    /// <param name="deliveryMethod">Способ доставки.</param>
    public void RefreshDeliveryMethod(IMailDeliveryMethod deliveryMethod)
    {
      var document = _obj.DocumentGroup.OfficialDocuments.FirstOrDefault();
      if (document != null)
      {
        var outgoingDocument = OutgoingDocumentBases.As(document);
        if (outgoingDocument != null && outgoingDocument.IsManyAddressees != true)
          document.DeliveryMethod = deliveryMethod;
      }
      
      _obj.DeliveryMethod = deliveryMethod;
    }
    
    /// <summary>
    /// Проверить наличие согласуемого документа в задаче и наличие хоть каких-то прав на него.
    /// </summary>
    /// <returns>True, если с документом можно работать.</returns>
    [Remote(IsPure = true)]
    public virtual bool HasDocumentAndCanRead()
    {
      return _obj.DocumentGroup.OfficialDocuments.Any();
    }
    
    /// <summary>
    /// Обновить доп. согласующих в задаче.
    /// </summary>
    /// <param name="approvers">Список доп. согласующих.</param>
    public void UpdateAdditionalApprovers(List<IRecipient> approvers)
    {
      var taskApprovers = _obj.AddApproversExpanded.Select(a => a.Approver).ToList();
      if (approvers.Except(taskApprovers).Any() || taskApprovers.Except(approvers).Any())
      {
        _obj.AddApproversExpanded.Clear();
        foreach (var approver in approvers)
          _obj.AddApproversExpanded.AddNew().Approver = approver;
      }
      
      var taskAddApprovers = _obj.AddApprovers.Select(a => a.Approver).ToList();
      if (approvers.Except(taskAddApprovers).Any() || taskAddApprovers.Except(approvers).Any())
      {
        _obj.AddApprovers.Clear();
        foreach (var approver in approvers)
          _obj.AddApprovers.AddNew().Approver = approver;
      }
      return;
    }
    
    /// <summary>
    /// Получить ожидаемый срок по задаче.
    /// </summary>
    /// <returns>Срок по задаче.</returns>
    [Remote(IsPure = true)]
    public DateTime? GetExpectedDate()
    {
      var stages = Functions.ApprovalTask.GetStages(_obj).Stages;
      return this.GetExpectedDate(null, stages);
    }
    
    /// <summary>
    /// Получить ожидаемый срок по задаче.
    /// </summary>
    /// <param name="currentAssignment">Текущее задание.</param>
    /// <param name="stages">Список этапов согласования.</param>
    /// <returns>Срок по задаче.</returns>
    [Remote(IsPure = true)]
    public DateTime? GetExpectedDate(IAssignment currentAssignment, List<Structures.Module.DefinedApprovalStageLite> stages)
    {
      if (_obj.ApprovalRule == null)
        return null;

      var currentStage = stages.FirstOrDefault(s => s.Number == (_obj.StageNumber ?? 0));
      var maxDeadline = Calendar.Now;
      var notStartedStages = stages;
      
      if (currentStage != null)
      {
        var assignments = Functions.ApprovalTask.GetTaskAssigments(_obj);
        if (currentAssignment != null)
          assignments.Add(currentAssignment);

        var assignmentsInProcess = assignments.Where(x => x.Status == Sungero.Docflow.ApprovalTask.Status.InProcess).ToList();
        if (assignmentsInProcess.Any())
        {
          var maxAsgDeadline = maxDeadline;
          foreach (var assignment in assignmentsInProcess.Where(d => d.Deadline.HasValue))
          {
            var currentDeadline = assignment.Deadline.Value.HasTime() ? assignment.Deadline.Value :
              assignment.Deadline.Value.EndOfDay().FromUserTime(assignment.Performer);
            if (currentDeadline > maxAsgDeadline)
              maxAsgDeadline = currentDeadline;
          }
          if (maxAsgDeadline > maxDeadline)
            maxDeadline = maxAsgDeadline;
          
          // Если задания идут по последовательному этапу, то могут быть созданы ещё задания, которые стоит учесть.
          var stage = currentStage.Stage;
          if (stage.Sequence == Sungero.Docflow.ApprovalStage.Sequence.Serially &&
              stage.Assignee == null &&
              stage.ApprovalRole == null)
          {
            var assignment = assignmentsInProcess.First();
            var allEmployees = Functions.ApprovalStage.GetStagePerformers(_obj, stage);
            var employeeWithAssignments = assignments
              .Where(a => Equals(a.BlockUid, assignment.BlockUid) &&
                     a.IterationId == assignment.IterationId && a.TaskStartId == assignment.TaskStartId)
              .Select(a => a.Performer)
              .ToList();
            var nextAssignees = allEmployees.Except(employeeWithAssignments);
            try
            {
              foreach (var assignee in nextAssignees)
              {
                if (stage.DeadlineInDays.HasValue)
                  maxDeadline = maxDeadline.AddWorkingDays(assignee, stage.DeadlineInDays.Value);
                if (stage.DeadlineInHours.HasValue)
                  maxDeadline = maxDeadline.AddWorkingHours(assignee, stage.DeadlineInHours.Value);
              }

            }
            catch (AppliedCodeException)
            {
              return null;
            }
          }
        }
        else
        {
          currentStage = stages.TakeWhile(s => s != currentStage).LastOrDefault();
        }
        
        if (currentStage != null)
          notStartedStages = notStartedStages.SkipWhile(s => s != currentStage).Skip(1).ToList();
      }
      
      foreach (IApprovalStage stage in notStartedStages.Select(s => s.Stage).ToList())
      {
        // Уведомления не влияют на срок.
        var notNotice = stage.StageType != Sungero.Docflow.ApprovalStage.StageType.Notice;
        // Этап с несколькими исполнителями.
        var hasAssignee = stage.Recipients.Any() || stage.ApprovalRoles.Any();
        // Этап согласования может быть без явных исполнителей, тогда надо проверить карточку на наличие доп. согласующих.
        if (!hasAssignee)
          hasAssignee = stage.AllowAdditionalApprovers == true && (_obj.AddApprovers.Any() || _obj.AddApproversExpanded.Any());
        
        var employees = new List<IEmployee>();
        // Этап с одним исполнителем.
        if (stage.Assignee != null || stage.ApprovalRole != null)
          employees.Add(Functions.ApprovalStage.GetStagePerformer(_obj, stage));
        else if (notNotice && hasAssignee)
        {
          // Из черновика задачи явно передаем список доп. согласующих,
          // в остальных случаях функция сама развернёт согласующих из задачки.
          var assigneeEmployees = _obj.Status != Docflow.ApprovalTask.Status.InProcess ?
            Functions.ApprovalStage.GetStagePerformers(_obj, stage, _obj.AddApprovers.Select(r => r.Approver).ToList()) :
            Functions.ApprovalStage.GetStagePerformers(_obj, stage);
          if (stage.Sequence == Sungero.Docflow.ApprovalStage.Sequence.Serially)
            employees.AddRange(assigneeEmployees);
          else
          {
            var maxAssigneeDeadline = maxDeadline;
            DateTime currentDeadline = maxAssigneeDeadline;
            foreach (var employee in assigneeEmployees)
            {
              try
              {
                // Должен быть перебор через foreach по всем исполнителям и для каждого сдвиг
                // foreach ... maxDeadline = maxDeadline.AddWorkingDays(recipient, stage.DeadlineInDays.Value) ...
                if (stage.DeadlineInDays.HasValue)
                  currentDeadline = maxDeadline.AddWorkingDays(employee, stage.DeadlineInDays.Value);
                if (stage.DeadlineInHours.HasValue)
                  currentDeadline = maxDeadline.AddWorkingHours(employee, stage.DeadlineInHours.Value);
                if (currentDeadline > maxAssigneeDeadline)
                  maxAssigneeDeadline = currentDeadline;
              }
              catch (AppliedCodeException)
              {
                return null;
              }
            }
            maxDeadline = maxAssigneeDeadline;
          }
        }
        
        foreach (var employee in employees)
        {
          try
          {
            // Должен быть перебор через foreach по всем исполнителям и для каждого сдвиг
            // foreach ... maxDeadline = maxDeadline.AddWorkingDays(recipient, stage.DeadlineInDays.Value) ...
            if (stage.DeadlineInDays.HasValue)
              maxDeadline = maxDeadline.AddWorkingDays(employee, stage.DeadlineInDays.Value);
            if (stage.DeadlineInHours.HasValue)
              maxDeadline = maxDeadline.AddWorkingHours(employee, stage.DeadlineInHours.Value);
          }
          catch (AppliedCodeException)
          {
            return null;
          }
        }
      }
      return maxDeadline;
    }
    
    /// <summary>
    /// Определить этапы для текущей задачи.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Отсортированный список этапов, подходящих по условиям.</returns>
    [Remote(PackResultEntityEagerly = true, IsPure = true)]
    public static Structures.Module.DefinedApprovalStages GetStages(IApprovalTask task)
    {
      if (task.ApprovalRule != null)
        return Functions.ApprovalRuleBase.GetStages(task.ApprovalRule, task.DocumentGroup.OfficialDocuments.FirstOrDefault(), task);
      else
        return Structures.Module.DefinedApprovalStages.Create(null, false, string.Empty);
    }

    /// <summary>
    /// Получить данные по этапам согласования для обновления формы.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stages">Список этапов согласования.</param>
    /// <returns>Структура с данными по этапам согласования.</returns>
    [Remote(IsPure = true)]
    public static Structures.ApprovalTask.RefreshParameters GetStagesInfoForRefresh(IApprovalTask task, List<Structures.Module.DefinedApprovalStageLite> stages)
    {
      var info = Structures.ApprovalTask.RefreshParameters.Create();
      if (task.ApprovalRule != null)
      {
        info.AddresseeIsEnabled = !task.ApprovalRule.Conditions.Any(c => c.Condition.ConditionType == Sungero.Docflow.Condition.ConditionType.Addressee);
      }
      var isExchange = task.DeliveryMethod != null && task.DeliveryMethod.Sid == Constants.MailDeliveryMethod.Exchange;
      info.ExchangeServiceIsEnabled = isExchange;
      var document = task.DocumentGroup.OfficialDocuments.FirstOrDefault();
      if (isExchange && OfficialDocuments.Is(document))
      {
        if (document.Versions.Any())
        {
          var isIncomingDocument = Docflow.PublicFunctions.OfficialDocument.Remote.CanSendAnswer(document);
          var isFormalizedDocument = Docflow.AccountingDocumentBases.Is(document) && Docflow.AccountingDocumentBases.As(document).IsFormalized == true;
          info.DeliveryMethodIsEnabled = !isIncomingDocument;
          info.ExchangeServiceIsEnabled = !(isIncomingDocument || isFormalizedDocument);
        }
      }

      var hasAddApproversStage = false;
      var hasReviewStage = false;
      var hasSignStage = false;
      var hasSendStage = false;
      var hasConditionWithSignatoryRole = false;
      var hasConditionWithAddresseeRole = false;
      var hasConditionWithSignAssistantRole = false;
      var hasConditionWithAddrAssistantRole = false;
      var hasConditionWithPrintRespRole = false;
      
      if (task.ApprovalRule != null)
      {
        hasAddApproversStage = stages.Any(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Approvers && s.Stage.AllowAdditionalApprovers == true);
        hasReviewStage = Functions.ApprovalRuleBase.HasApprovalStage(task.ApprovalRule, Docflow.ApprovalStage.StageType.Review, document, stages);
        hasSignStage = Functions.ApprovalRuleBase.HasApprovalStage(task.ApprovalRule, Docflow.ApprovalStage.StageType.Sign, document, stages);
        hasSendStage = Functions.ApprovalRuleBase.HasApprovalStage(task.ApprovalRule, Docflow.ApprovalStage.StageType.Sending, document, stages) ||
          Functions.ApprovalRuleBase.HasApprovalCondition(task.ApprovalRule, document, task, Docflow.ConditionBase.ConditionType.DeliveryMethod);
        
        if (document != null)
        {
          // Список достижимых условий в правиле согласования.
          var conditions = Functions.ApprovalRuleBase.GetConditions(task.ApprovalRule, document, task);
          
          hasConditionWithSignatoryRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(task.ApprovalRule, conditions, Docflow.ApprovalRoleBase.Type.Signatory);
          hasConditionWithAddresseeRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(task.ApprovalRule, conditions, Docflow.ApprovalRoleBase.Type.Addressee);
          hasConditionWithSignAssistantRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(task.ApprovalRule, conditions, Docflow.ApprovalRoleBase.Type.SignAssistant);
          hasConditionWithAddrAssistantRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(task.ApprovalRule, conditions, Docflow.ApprovalRoleBase.Type.AddrAssistant);
          hasConditionWithPrintRespRole = Functions.ApprovalRuleBase.HasApprovalConditionWithRole(task.ApprovalRule, conditions, Docflow.ApprovalRoleBase.Type.PrintResp);
        }
      }
      
      info.AddApproversIsVisible = hasAddApproversStage;
      info.AddresseeIsVisible = hasReviewStage || hasConditionWithAddresseeRole || hasConditionWithAddrAssistantRole;
      info.SignatoryIsVisible = hasSignStage || hasConditionWithSignatoryRole || hasConditionWithSignAssistantRole || hasConditionWithPrintRespRole;
      info.DeliveryMethodIsVisible = hasSendStage;
      info.ExchangeServiceIsVisible = hasSendStage;
      
      info.AddresseeIsRequired = hasReviewStage;
      info.SignatoryIsRequired = hasSignStage;
      info.ExchangeServiceIsRequired = task.DeliveryMethod != null && task.DeliveryMethod.Sid == Constants.MailDeliveryMethod.Exchange;
      
      return info;
    }

    /// <summary>
    /// Получить данные по этапам согласования для обновления формы.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Структура с данными по этапам согласования.</returns>
    [Remote(IsPure = true)]
    public static Structures.ApprovalTask.RefreshParameters GetStagesInfoForRefresh(IApprovalTask task)
    {
      var stages = Functions.ApprovalTask.GetStages(task).Stages;
      return Functions.ApprovalTask.GetStagesInfoForRefresh(task, stages);
    }
    
    /// <summary>
    /// Получить список доступных исполнителей доработки.
    /// </summary>
    /// <returns>Список доступных исполнителей доработки.</returns>
    [Public, Remote(IsPure = true)]
    public virtual List<IEmployee> GetReworkPerformers()
    {
      var recipients = this.GetTaskAssignees(false);
      var stage = _obj.ApprovalRule.Stages.Where(s => s.Number == _obj.StageNumber).FirstOrDefault();
      if (stage != null && stage.Stage.ReworkPerformerType == ReworkPerformerType.EmployeeRole)
        recipients.Add(stage.Stage.ReworkPerformer);
      
      if (_obj.ApprovalRule.ReworkPerformerType == Docflow.ApprovalRuleBase.ReworkPerformerType.EmployeeRole && _obj.ApprovalRule.ReworkPerformer != null)
        recipients.Add(_obj.ApprovalRule.ReworkPerformer);
      
      var performers = Docflow.Functions.Module.GetEmployeesFromRecipients(recipients);
      performers.Add(Employees.As(_obj.Author));

      var reworkPerformers = ApprovalReworkAssignments
        .GetAll()
        .Where(a => Equals(a.Task, _obj))
        .Where(a => a.Created > _obj.Started)
        .Select(a => Employees.As(a.Performer))
        .ToList();
      
      performers.AddRange(reworkPerformers);
      
      if (_obj.ApprovalRule.ReworkPerformerType == Docflow.ApprovalRuleBase.ReworkPerformerType.ApprovalRole && _obj.ApprovalRule.ReworkApprovalRole != null)
      {
        var rolePerformer = Functions.ApprovalRoleBase.GetRolePerformer(_obj.ApprovalRule.ReworkApprovalRole, _obj);
        if (rolePerformer != null)
          performers.Add(rolePerformer);
      }
      if (stage != null && stage.Stage.ReworkPerformerType == ReworkPerformerType.ApprovalRole)
      {
        var stagePerformer = Functions.ApprovalRoleBase.GetRolePerformer(stage.Stage.ReworkApprovalRole, _obj);
        if (stagePerformer != null)
          performers.Add(stagePerformer);
      }
      
      return performers
        .Distinct()
        .OrderBy(p => p.Name)
        .ToList();
    }
    
    /// <summary>
    /// Вычислить исполнителя задания на доработку.
    /// </summary>
    /// <param name="stage">Этап согласования.</param>
    /// <returns>Исполнитель.</returns>
    public virtual IEmployee GetReworkPerformer(IApprovalStage stage)
    {
      if (_obj.ReworkPerformer != null)
        return _obj.ReworkPerformer;
      
      if (stage != null && stage.ReworkPerformerType != Docflow.ApprovalStage.ReworkPerformerType.FromRule)
      {
        if (stage.ReworkPerformerType == Docflow.ApprovalStage.ReworkPerformerType.EmployeeRole)
        {
          if (Employees.Is(stage.ReworkPerformer))
            return Employees.As(stage.ReworkPerformer);
          else if (Roles.Is(stage.ReworkPerformer))
          {
            var performer = Roles.As(stage.ReworkPerformer).RecipientLinks.FirstOrDefault();
            if (performer != null && Employees.Is(performer.Member))
              return Employees.As(performer.Member);
          }
        }
        else if (stage.ReworkPerformerType == Docflow.ApprovalStage.ReworkPerformerType.ApprovalRole)
        {
          var rolePerformer = Functions.ApprovalRoleBase.GetRolePerformer(stage.ReworkApprovalRole, _obj);
          if (rolePerformer != null)
            return rolePerformer;
        }
        else if (stage.ReworkPerformerType == Docflow.ApprovalStage.ReworkPerformerType.Author)
          return Employees.As(_obj.Author);
      }
      
      if (_obj.ApprovalRule.ReworkPerformerType == Docflow.ApprovalRuleBase.ReworkPerformerType.ApprovalRole && _obj.ApprovalRule.ReworkApprovalRole != null)
      {
        var rolePerformer = Functions.ApprovalRoleBase.GetRolePerformer(_obj.ApprovalRule.ReworkApprovalRole, _obj);
        if (rolePerformer != null)
          return rolePerformer;
      }
      
      if (_obj.ApprovalRule.ReworkPerformerType == Docflow.ApprovalRuleBase.ReworkPerformerType.EmployeeRole && _obj.ApprovalRule.ReworkPerformer != null)
      {
        if (Employees.Is(_obj.ApprovalRule.ReworkPerformer))
          return Employees.As(_obj.ApprovalRule.ReworkPerformer);
        else if (Roles.Is(_obj.ApprovalRule.ReworkPerformer))
        {
          var performer = Roles.As(_obj.ApprovalRule.ReworkPerformer).RecipientLinks.FirstOrDefault();
          if (performer != null && Employees.Is(performer.Member))
            return Employees.As(performer.Member);
        }
      }
      
      return Employees.As(_obj.Author);
    }
    
    /// <summary>
    /// Получить параметры для отправки на доработку.
    /// </summary>
    /// <param name="stageNumber">Номер этапа.</param>
    /// <returns>Параметры доработки.</returns>
    [Remote]
    public virtual Structures.ApprovalTask.ReworkParameters GetReworkParameters(int stageNumber)
    {
      var reworkParameters = Structures.ApprovalTask.ReworkParameters.Create();
      reworkParameters.AllowChangeReworkPerformer = false;
      reworkParameters.AllowViewReworkPerformer = false;
      reworkParameters.AllowSendToRework = false;
      var item = _obj.ApprovalRule.Stages.Where(s => s.Number == stageNumber).FirstOrDefault();
      if (item == null)
        return reworkParameters;
      var stage = item.Stage;
      reworkParameters.AllowChangeReworkPerformer = stage.AllowChangeReworkPerformer ?? false;
      reworkParameters.AllowViewReworkPerformer = stage.AllowChangeReworkPerformer ?? false;
      reworkParameters.AllowSendToRework = stage.AllowSendToRework ?? false;
      return reworkParameters;
    }
    
    /// <summary>
    /// Получить исполнителя последнего задания.
    /// </summary>
    /// <returns>Исполнитель последнего задания.</returns>
    public virtual IUser GetLastAssignmentPerformer()
    {
      IUser performer = null;
      // Получить предыдущее задание.
      var lastAssignment = Functions.ApprovalTask.GetLastTaskAssigment(_obj, null);
      
      // Если это подписание, то инициатор - подписывающий.
      if (ApprovalSigningAssignments.Is(lastAssignment))
      {
        var signAssignment = ApprovalSigningAssignments.As(lastAssignment);
        performer = signAssignment.Performer;
      }
      // Если это рассмотрение, то инициатор - адресат.
      if (ApprovalReviewAssignments.Is(lastAssignment))
      {
        var reviewAssignment = ApprovalReviewAssignments.As(lastAssignment);
        performer = reviewAssignment.Performer;
      }
      if (ApprovalCheckReturnAssignments.Is(lastAssignment))
      {
        var checkAssignment = ApprovalCheckReturnAssignments.GetAll()
          .Where(ass => Equals(ass.Task, _obj) && ass.Result == Docflow.ApprovalCheckReturnAssignment.Result.NotSigned)
          .OrderByDescending(o => o.Completed).FirstOrDefault();
        performer = checkAssignment.Performer;
      }
      if (ApprovalAssignments.Is(lastAssignment))
      {
        var apprAssignment = ApprovalAssignments.GetAll()
          .Where(ass => Equals(ass.Task, _obj) && ass.Result == Docflow.ApprovalAssignment.Result.ForRevision)
          .OrderByDescending(o => o.Completed).FirstOrDefault();
        performer = apprAssignment.Performer;
      }
      if (ApprovalManagerAssignments.Is(lastAssignment))
      {
        var apprAssignment = ApprovalManagerAssignments.GetAll()
          .Where(ass => Equals(ass.Task, _obj) && ass.Result == Docflow.ApprovalManagerAssignment.Result.ForRevision)
          .OrderByDescending(o => o.Completed).FirstOrDefault();
        performer = apprAssignment.Performer;
      }
      
      return performer ?? _obj.Author;
    }

  }
}