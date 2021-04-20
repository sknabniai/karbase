﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sungero.Company;
using Sungero.Content;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow;
using Sungero.Domain.Shared;
using Sungero.Metadata.Services;
using Sungero.Reporting.Client;
using Sungero.Reporting.Shared;
using Sungero.Workflow;

namespace Sungero.RecordManagement.Client
{
  public class ModuleFunctions
  {
    /// <summary>
    /// Показать настройки текущего пользователя.
    /// </summary>
    public static void ShowCurrentPersonalSettings()
    {
      var personalSettings = Docflow.PublicFunctions.PersonalSetting.GetPersonalSettings(null);
      if (personalSettings != null)
        personalSettings.Show();
      else
        Dialogs.ShowMessage(Resources.FailedGetSettings, MessageType.Error);
    }
    
    /// <summary>
    /// Перегрузка открытия отчет "Контроль исполнения поручений" из виджета за указанный месяц.
    /// </summary>
    /// <param name="currentPeriod">Дата.</param>
    /// <param name="performerParam">Исполнитель, указанный в параметрах виджета.</param>
    public static void ShowActionItemsExecutionReport(DateTime currentPeriod, Enumeration performerParam)
    {
      // Текущий период.
      var periodBegin = currentPeriod.BeginningOfMonth();
      var periodEnd = currentPeriod.EndOfMonth();
      
      var report = RecordManagement.Reports.GetActionItemsExecutionReport();
      report.BeginDate = periodBegin;
      report.EndDate = periodEnd.EndOfDay();
      report.ClientEndDate = periodEnd;

      if (performerParam == RecordManagement.Widgets.ActionItemCompletionGraph.Performer.Author)
        report.Author = Company.Employees.Current;
      
      report.Open();
    }
    
    /// <summary>
    /// Диалог с запросом параметров для отчетов по журналам регистрации.
    /// </summary>
    /// <param name="reportName">Наименование отчета.</param>
    /// <param name="direction">Направление документопотока журнала.</param>
    /// <param name="documentRegisterValue">Журнал.</param>
    /// <param name="helpCode">Код справки.</param>
    /// <returns>Возвращает структуру формата - запустить отчет, дата начала, дата окончания, журнал.</returns>
    public static Structures.Module.DocumentRegisterReportParametrs ShowDocumentRegisterReportDialog(string reportName, Enumeration direction,
                                                                                                     IDocumentRegister documentRegisterValue,
                                                                                                     string helpCode)
    {
      var personalSettings = Docflow.PublicFunctions.PersonalSetting.GetPersonalSettings(Employees.Current);
      var dialog = Dialogs.CreateInputDialog(reportName);
      dialog.HelpCode = helpCode;

      var settingsBeginDate = Docflow.PublicFunctions.PersonalSetting.GetStartDate(personalSettings);
      var beginDate = dialog.AddDate(Resources.StartDate, true, settingsBeginDate ?? Calendar.UserToday);
      var settingsEndDate = Docflow.PublicFunctions.PersonalSetting.GetEndDate(personalSettings);
      var endDate = dialog.AddDate(Resources.EndDate, true, settingsEndDate ?? Calendar.UserToday);
      
      INavigationDialogValue<IDocumentRegister> documentRegister = null;
      if (documentRegisterValue == null)
      {
        var documentRegisters = Functions.Module.Remote.GetFilteredDocumentRegistersForReport(direction);
        IDocumentRegister defaultDocumentRegister = null;
        if (personalSettings != null)
        {
          if (direction == Docflow.DocumentRegister.DocumentFlow.Incoming)
            defaultDocumentRegister = personalSettings.IncomingDocRegister;
          else if (direction == Docflow.DocumentRegister.DocumentFlow.Outgoing)
            defaultDocumentRegister = personalSettings.OutgoingDocRegister;
          else
            defaultDocumentRegister = personalSettings.InnerDocRegister;
        }
        
        if (documentRegisters.Count == 1)
          documentRegister = dialog.AddSelect(Docflow.Resources.DocumentRegister, true, documentRegisters.FirstOrDefault()).From(documentRegisters);
        else
          documentRegister = dialog.AddSelect(Docflow.Resources.DocumentRegister, true, defaultDocumentRegister).From(documentRegisters);
      }
      
      dialog.SetOnButtonClick((args) =>
                              {
                                Docflow.PublicFunctions.Module.CheckReportDialogPeriod(args, beginDate, endDate);
                              });
      
      dialog.Buttons.AddOkCancel();
      dialog.Buttons.Default = DialogButtons.Ok;
      if (dialog.Show() == DialogButtons.Ok)
      {
        if (documentRegisterValue == null)
          documentRegisterValue = documentRegister.Value;
        return Structures.Module.DocumentRegisterReportParametrs.Create(true, beginDate.Value, endDate.Value, documentRegisterValue);
      }
      else
      {
        documentRegisterValue = null;
        return Structures.Module.DocumentRegisterReportParametrs.Create(false, null, null, null);
      }
    }
    
    #region Фильтрация кешированных справочников

    /// <summary>
    ///  Получить отфильтрованные виды документов.
    /// </summary>
    /// <param name="direction">Документопоток вида документа.</param>
    /// <returns>Виды документов.</returns>.
    public static List<IDocumentKind> GetFilteredDocumentKinds(Enumeration direction)
    {
      if (DocumentKinds.Info.IsCacheable)
      {
        if (direction == Docflow.DocumentKind.DocumentFlow.Incoming)
          return DocumentKinds.GetAllCached(d => d.DocumentFlow.Value == Docflow.DocumentKind.DocumentFlow.Incoming).ToList();
        else if (direction == Docflow.DocumentKind.DocumentFlow.Outgoing)
          return DocumentKinds.GetAllCached(d => d.DocumentFlow.Value == Docflow.DocumentKind.DocumentFlow.Outgoing).ToList();
        else if (direction == Docflow.DocumentKind.DocumentFlow.Inner)
          return DocumentKinds.GetAllCached(d => d.DocumentFlow.Value == Docflow.DocumentKind.DocumentFlow.Inner).ToList();
        else if (direction == Docflow.DocumentKind.DocumentFlow.Contracts)
          return DocumentKinds.GetAllCached(d => d.DocumentFlow.Value == Docflow.DocumentKind.DocumentFlow.Contracts).ToList();
        else
          return null;
      }
      else
        return Functions.Module.Remote.GetFilteredDocumentKinds(direction);
    }

    #endregion

    /// <summary>
    /// Показать диалог подтверждения выполнения без создания поручений.
    /// </summary>
    /// <param name="assignment">Задание, которое выполняется.</param>
    /// <param name="document">Документ.</param>
    /// <param name="e">Аргументы.</param>
    /// <returns>True, если диалог был, иначе false.</returns>
    public static bool ShowConfirmationDialogCreationActionItem(IAssignment assignment, IOfficialDocument document, Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      var reviewTask = DocumentReviewTasks.As(assignment.Task);
      var hasSubActionItem = Functions.ActionItemExecutionTask.Remote.HasSubActionItems(assignment.Task, Workflow.Task.Status.InProcess, reviewTask.Addressee);
      
      if (hasSubActionItem)
        return false;
      
      var dialogText = ReviewResolutionAssignments.Is(assignment) ?
        Docflow.Resources.ExecuteWithoutCreatingActionItemFromAddressee : Docflow.Resources.ExecuteWithoutCreatingActionItem;
      var dialog = Dialogs.CreateTaskDialog(dialogText, MessageType.Question);
      dialog.Buttons.AddYes();
      dialog.Buttons.Default = DialogButtons.Yes;
      var createActionItemButton = dialog.Buttons.AddCustom(Docflow.Resources.CreateActionItem);
      dialog.Buttons.AddNo();
      
      var result = dialog.Show();
      if (result == DialogButtons.Yes)
        return true;
      
      if (result == DialogButtons.Cancel || result == DialogButtons.No)
        e.Cancel();
      
      assignment.Save();
      var assignedBy = reviewTask.Addressee;
      var resolution = ReviewResolutionAssignments.Is(assignment) ? ReviewResolutionAssignments.As(assignment).ResolutionText : assignment.ActiveText;
      var actionItem = Functions.Module.Remote.CreateActionItemExecutionWithResolution(document, assignment.Id, resolution, assignedBy);
      actionItem.IsDraftResolution = false;
      actionItem.ShowModal();
      
      hasSubActionItem = Functions.ActionItemExecutionTask.Remote.HasSubActionItems(assignment.Task, Workflow.Task.Status.InProcess, reviewTask.Addressee);
      if (hasSubActionItem)
        return true;
      
      var hasDraftSubActionItem = Functions.ActionItemExecutionTask.Remote.HasSubActionItems(assignment.Task, Workflow.Task.Status.Draft, reviewTask.Addressee);
      e.AddError(hasDraftSubActionItem ? Docflow.Resources.AllCreatedActionItemsShouldBeStarted : Docflow.Resources.CreatedActionItemExecutionNeeded);
      e.Cancel();
      return true;
    }
    
    /// <summary>
    /// Паблик обертка для получения отчета.
    /// </summary>
    /// <returns>Отчет.</returns>
    [Public]
    public Sungero.Reporting.IReport GetOutgoingDocumentsReport()
    {
      return Reports.GetOutgoingDocumentsReport();
    }
    
    /// <summary>
    /// Паблик обертка для получения отчета.
    /// </summary>
    /// <returns>Отчет.</returns>
    [Public]
    public Sungero.Reporting.IReport GetInternalDocumentsReport()
    {
      return Reports.GetInternalDocumentsReport();
    }
    
    /// <summary>
    /// Паблик обертка для получения отчета.
    /// </summary>
    /// <returns>Отчет.</returns>
    [Public]
    public Sungero.Reporting.IReport GetIncomingDocumentsReport()
    {
      return Reports.GetIncomingDocumentsReport();
    }
    
    /// <summary>
    /// Паблик обертка для получения отчета.
    /// </summary>
    /// <returns>Отчет.</returns>
    [Public]
    public Sungero.Reporting.IReport GetIncomingDocumentsProcessingReport()
    {
      return Reports.GetIncomingDocumentsProcessingReport();
    }
    
    /// <summary>
    /// Паблик обертка для получения отчета.
    /// </summary>
    /// <returns>Отчет.</returns>
    [Public]
    public Sungero.Reporting.IReport GetDocumentReturnReport()
    {
      return Reports.GetDocumentReturnReport();
    }
    
    /// <summary>
    /// Получить отчет "Контроль ознакомления".
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Отчет.</returns>
    [Public]
    public virtual Sungero.Reporting.IReport GetAcquaintanceReport(IAcquaintanceTask task)
    {
      var report = Reports.GetAcquaintanceReport();
      report.Task = task;
      return report;
    }
    
    /// <summary>
    /// Получить отчет "Бланк ознакомления".
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Отчет.</returns>
    [Public]
    public virtual Sungero.Reporting.IReport GetAcquaintanceFormReport(IAcquaintanceTask task)
    {
      var report = Reports.GetAcquaintanceFormReport();
      report.Task = task;
      return report;
    }
    
    /// <summary>
    /// Получить отчет "Контроль ознакомления".
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Отчет.</returns>
    [Public]
    public virtual Sungero.Reporting.IReport GetAcquaintanceReport(IOfficialDocument document)
    {
      var report = Reports.GetAcquaintanceReport();
      report.Document = document;
      return report;
    }
  }
}