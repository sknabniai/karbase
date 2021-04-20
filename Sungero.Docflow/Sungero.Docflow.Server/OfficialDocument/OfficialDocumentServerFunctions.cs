﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommonLibrary;
using Sungero.Company;
using Sungero.Content;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.CoreEntities.Server;
using Sungero.Docflow.ApprovalStage;
using Sungero.Docflow.OfficialDocument;
using Sungero.Domain.Shared;
using Sungero.Metadata;
using Sungero.Parties;
using Sungero.Workflow;
using DeclensionCase = Sungero.Core.DeclensionCase;

namespace Sungero.Docflow.Server
{
  partial class OfficialDocumentFunctions
  {

    #region Контрол "Состояние"

    /// <summary>
    /// Построить модель состояния документа.
    /// </summary>
    /// <returns>Контрол состояния.</returns>
    [Remote(IsPure = true)]
    public Sungero.Core.StateView GetStateViewXml()
    {
      // Переполучить электронный документ для отображения ПО, когда документ еще не сохранен (после смены типа).
      var document = ElectronicDocuments.GetAll(a => a.Id == _obj.Id).FirstOrDefault();
      if (document != null)
        return GetStateView(document);
      
      return GetStateView(_obj);
    }
    
    /// <summary>
    /// Построить модель состояния документа.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Схема модели состояния.</returns>
    /// <remarks>По идее, одноименная функция ожидается у всех сущностей, которым нужно представление состояния.</remarks>
    [Public]
    public static Sungero.Core.StateView GetStateView(IElectronicDocument document)
    {
      var stateView = StateView.Create();
      stateView.AddDefaultLabel(OfficialDocuments.Resources.StateViewDefault);
      AddTasksViews(stateView, document);
      stateView.IsPrintable = true;
      return stateView;
    }
    
    /// <summary>
    /// Построить сводку по документу.
    /// </summary>
    /// <returns>Сводка по документу.</returns>
    [Remote(IsPure = true)]
    public virtual StateView GetDocumentSummary()
    {
      var documentSummary = StateView.Create();
      documentSummary.AddDefaultLabel(string.Empty);
      return documentSummary;
    }
    
    /// <summary>
    /// Получить отображение суммы документа.
    /// </summary>
    /// <param name="totalAmount">Значение суммы.</param>
    /// <param name="currency">Валюта.</param>
    /// <returns>Отображение суммы документа.</returns>
    protected virtual string GetTotalAmountDocumentSummary(double? totalAmount, Commons.ICurrency currency)
    {
      var canRead = _obj.AccessRights.CanRead();
      var amount = "-";
      
      if (canRead && totalAmount.HasValue && currency != null)
      {
        var currencyShortName = currency.ShortName.Trim();
        var currencyName = string.Empty;
        if (currencyShortName.EndsWith("."))
        {
          currencyName = currencyShortName;
        }
        else
        {
          var lastTwoDigits = (int)(totalAmount.Value % 100);
          currencyName = StringUtils.NumberDeclension(lastTwoDigits,
                                                      currencyShortName,
                                                      Sungero.Core.CaseConverter.ConvertCurrencyNameToTargetDeclension(currencyShortName, Sungero.Core.DeclensionCase.Genitive),
                                                      Sungero.Core.CaseConverter.ConvertCurrencyNameToTargetDeclension(currencyShortName.Pluralize(), Sungero.Core.DeclensionCase.Genitive));
        }
        
        amount = string.Format("{0} {1}", Math.Truncate(totalAmount.Value).ToString("N0"), currencyName);
      }
      
      return amount;
    }
    
    /// <summary>
    /// Добавить информацию о задачах, в которые вложен документ.
    /// </summary>
    /// <param name="stateView">Схема представления.</param>
    /// <param name="document">Документ.</param>
    private static void AddTasksViews(StateView stateView, IElectronicDocument document)
    {
      var tasks = Tasks.GetAll()
        .Where(task => task.AttachmentDetails
               .Any(a => a.AttachmentTypeGuid == document.GetEntityMetadata().GetOriginal().NameGuid &&
                    a.AttachmentId == document.Id))
        .OrderBy(task => task.Created)
        .ToList();
      
      foreach (var task in tasks)
      {
        if (stateView.Blocks.Any(b => b.HasEntity(task)))
          continue;
        
        AddTaskViewXml(stateView, task, document);
      }
    }
    
    /// <summary>
    /// Построение модели задачи, в которую вложен документ.
    /// </summary>
    /// <param name="stateView">Схема представления.</param>
    /// <param name="task">Задача.</param>
    /// <param name="document">Документ.</param>
    private static void AddTaskViewXml(StateView stateView, ITask task, IElectronicDocument document)
    {
      // Добавить предметное отображение для простых задач.
      if (SimpleTasks.Is(task))
      {
        AddSimpleTaskView(stateView, SimpleTasks.As(task));
        return;
      }
      
      // Добавить предметное отображение для прикладных задач.
      var taskStateView = Functions.Module.GetServerEntityFunctionResult(task, "GetStateView", new List<object>() { document });
      if (taskStateView != null)
      {
        // Избавиться от дублирующих блоков, если таковые были.
        List<StateBlock> blockWhiteList = new List<StateBlock>() { };
        
        foreach (var block in ((StateView)taskStateView).Blocks)
        {
          if (block.Entity == null || !stateView.Blocks.Any(b => b.HasEntity(block.Entity)))
            blockWhiteList.Add(block);
        }
        
        foreach (var block in blockWhiteList)
          stateView.AddBlock(block);
      }
    }
    
    /// <summary>
    /// Добавить блок информации о действии.
    /// </summary>
    /// <param name="stateView">Схема представления.</param>
    /// <param name="user">Пользователь, выполнивший действие.</param>
    /// <param name="text">Текст действия.</param>
    /// <param name="date">Дата действия.</param>
    /// <param name="entity">Сущность, над которой было совершено действие.</param>
    /// <param name="comment">Примечание к действию.</param>
    /// <param name="substituted">Замещающий.</param>
    [Public]
    public static void AddUserActionBlock(object stateView, IUser user, string text, DateTime date,
                                          IEntity entity, string comment, IUser substituted)
    {
      StateBlock block;
      if (stateView is StateBlock)
        block = (stateView as StateBlock).AddChildBlock();
      else
        block = (stateView as StateView).AddBlock();
      block.Entity = entity;
      block.DockType = DockType.Bottom;
      block.AssignIcon(StateBlockIconType.User, StateBlockIconSize.Small);
      block.ShowBorder = false;
      var userActionText = string.Format("{0}. ", GetUserActionText(user, text, substituted));
      block.AddLabel(userActionText);
      
      var style = Functions.Module.CreateStyle(false, true);
      block.AddLabel(string.Format("{0}: {1}", OfficialDocuments.Resources.StateViewDate.ToString(), Functions.Module.ToShortDateShortTime(date.ToUserTime())), style);
      
      comment = Functions.Module.GetFormatedUserText(comment);
      if (!string.IsNullOrWhiteSpace(comment))
      {
        block.AddLineBreak();
        block.AddEmptyLine(1);
        block.AddLabel(comment, style);
      }
    }
    
    /// <summary>
    /// Получить автора задачи (автор, либо кто за кого выполнил).
    /// </summary>
    /// <param name="author">Автор.</param>
    /// <param name="startedBy">Выполнивший.</param>
    /// <returns>Фамилия инициалы автора, либо фамилия инициалы с учетом замещения.</returns>
    [Public]
    public static string GetAuthor(IUser author, IUser startedBy)
    {
      // Костыль для системных пользователей.
      if (!Employees.Is(author))
        return author.IsSystem == true ? OfficialDocuments.Resources.StateViewSystem.ToString() : author.Name;
      
      if (Equals(author, startedBy) || startedBy == null)
        return Company.PublicFunctions.Employee.GetShortName(Employees.As(author), false);

      var started = OfficialDocuments.Resources.StateViewSystem.ToString();
      if (Employees.Is(startedBy))
      {
        started = Company.PublicFunctions.Employee.GetShortName(Employees.As(startedBy), false);
      }
      
      return started + OfficialDocuments.Resources.StateViewInstead.ToString() + Company.PublicFunctions.Employee.GetShortName(Employees.As(author), DeclensionCase.Accusative, false);
    }
    
    /// <summary>
    /// Построить текст действия от пользователя.
    /// </summary>
    /// <param name="user">Пользователь.</param>
    /// <param name="text">Текст.</param>
    /// <param name="substituted">Замещаемый.</param>
    /// <returns>Сформированная строка вида "Пользователь (за замещаемого). Текст действия.".</returns>
    [Public]
    public static string GetUserActionText(IUser user, string text, IUser substituted)
    {
      return string.Format("{0}. {1}", GetAuthor(user, substituted).TrimEnd('.'), text.TrimEnd('.'));
    }

    /// <summary>
    /// Подсчет рабочих дней в промежутке времени.
    /// </summary>
    /// <param name="startDate">Начало.</param>
    /// <param name="endDate">Окончание.</param>
    /// <param name="user">Пользователь.</param>
    /// <returns>Количество рабочих дней.</returns>
    [Public]
    public static int DurationInWorkdays(DateTime startDate, DateTime endDate, IUser user)
    {
      var start = Functions.Module.GetDateWithTime(startDate, user);
      var end = Functions.Module.GetDateWithTime(endDate, user);
      var days = (end - start).TotalDays;
      var calendarDays = (int)(endDate.Date - startDate.Date).TotalDays;
      var workdays = WorkingTime.GetDurationInWorkingDays(start, end);
      if (days < 0 || Equals(start, end))
        return -1;
      if (calendarDays == 0 && workdays == 1)
        return 0;
      return Math.Min(calendarDays, workdays);
    }

    /// <summary>
    /// Подсчет рабочих часов в промежутке времени.
    /// </summary>
    /// <param name="startDate">Начало.</param>
    /// <param name="endDate">Окончание.</param>
    /// <param name="user">Пользователь.</param>
    /// <returns>Количество рабочих часов.</returns>
    /// <remarks>Только в рамках одного дня.</remarks>
    [Public]
    private static int DurationInWorkhours(DateTime startDate, DateTime endDate, IUser user)
    {
      var workday = CoreEntities.WorkingTime.GetAllCachedByYear(startDate.Year).Where(c => startDate.Year == c.Year)
        .SelectMany(y => y.Day)
        .ToList()
        .SingleOrDefault(d => startDate.Date == d.Day.Date && d.Day.IsWorkingDay(user));
      
      if (workday == null)
        return 0;

      var start = Functions.Module.GetDateWithTime(startDate, user);
      var end = Functions.Module.GetDateWithTime(endDate, user);
      var duration = WorkingTime.GetDurationInWorkingHours(start, end, user);
      var result = Math.Round(duration, 0);
      return result < 1 ? 1 : (int)result;
    }

    /// <summary>
    /// Добавить в заголовок информацию о задержке выполнения.
    /// </summary>
    /// <param name="block">Блок схемы.</param>
    /// <param name="deadline">Планируемый срок выполнения.</param>
    /// <param name="user">Исполнитель.</param>
    [Public]
    public static void AddDeadlineHeaderToRight(Sungero.Core.StateBlock block, DateTime deadline, IUser user)
    {
      var now = Calendar.Now;
      var delayInDays = DurationInWorkdays(deadline, now, user);
      var delayInHours = 0;
      
      if (delayInDays < 0)
        return;
      
      if (delayInDays < 1)
      {
        delayInHours = DurationInWorkhours(deadline, now, user);
        if (delayInHours == 0)
          return;
      }
      
      var delay = delayInDays < 1 ?
        Functions.Module.GetNumberDeclination(delayInHours,
                                              Resources.StateViewHour,
                                              Resources.StateViewHourGenetive,
                                              Resources.StateViewHourPlural) :
        Functions.Module.GetNumberDeclination(delayInDays,
                                              Resources.StateViewDay,
                                              Resources.StateViewDayGenetive,
                                              Resources.StateViewDayPlural);
      
      var label = string.Format("{0} {1} {2}", OfficialDocuments.Resources.StateViewDelay, delayInDays < 1 ? delayInHours : delayInDays, delay);
      var style = Functions.Module.CreateStyle(Sungero.Core.Colors.Common.Red);
      
      // Добавить колонку справа, если всего одна колонка (main).
      var rightContent = block.Contents.LastOrDefault();
      if (block.Contents.Count() <= 1)
        rightContent = block.AddContent();
      else
        rightContent.AddLineBreak();
      
      rightContent.AddLabel(label, style);
    }
    
    /// <summary>
    /// Добавить предметное отображение простой задачи.
    /// </summary>
    /// <param name="stateView">Схема представления.</param>
    /// <param name="task">Задача.</param>
    private static void AddSimpleTaskView(StateView stateView, ISimpleTask task)
    {
      if (task == null)
        return;
      
      // Не добавлять блок, если нет заданий. Черновик - исключение.
      var assignments = new List<IAssignment>() { };
      assignments.AddRange(SimpleAssignments.GetAll().Where(a => Equals(a.Task, task)).ToList());
      assignments.AddRange(ReviewAssignments.GetAll().Where(a => Equals(a.Task, task) && a.Result == null).ToList());
      if (!assignments.Any() && task.Status != Workflow.Task.Status.Draft)
        return;
      
      // Добавить блок информации о действии.
      if (task.Started.HasValue)
        AddUserActionBlock(stateView, task.Author, OfficialDocuments.Resources.StateViewTaskSent, task.Started.Value, task, string.Empty, task.StartedBy);
      else
        AddUserActionBlock(stateView, task.Author, ApprovalTasks.Resources.StateViewTaskDrawCreated, task.Created.Value, task, string.Empty, task.Author);
      
      // Добавить блок информации по задаче.
      var mainBlock = GetSimpleTaskMainBlock(task);
      stateView.AddBlock(mainBlock);
      
      // Маршрут.
      var iterations = Functions.Module.GetIterationDates(task);
      foreach (var iteration in iterations)
      {
        var date = iteration.Date;
        var hasReworkBefore = iteration.IsRework;
        var hasRestartBefore = iteration.IsRestart;
        
        var nextIteration = iterations.Where(d => d.Date > date).FirstOrDefault();
        var nextDate = nextIteration != null ? nextIteration.Date : Calendar.Now;
        
        // Получить задания в рамках круга согласования.
        var iterationAssignments = assignments
          .Where(a => a.Created >= date && a.Created < nextDate)
          .OrderBy(a => a.Created)
          .ToList();
        
        if (!iterationAssignments.Any())
          continue;
        
        if (hasReworkBefore || hasRestartBefore)
        {
          var activeText = task.Texts
            .Where(t => t.Modified >= date)
            .OrderBy(t => t.Created)
            .FirstOrDefault();
          
          var comment = activeText != null ? activeText.Body : string.Empty;
          var started = activeText != null ? activeText.Modified : task.Started;
          
          var header = hasReworkBefore ? OfficialDocuments.Resources.StateViewTaskSentForRevision : OfficialDocuments.Resources.StateViewTaskSentAfterRestart;
          AddUserActionBlock(mainBlock, task.Author, header, started.Value, task, comment, task.StartedBy);
        }
        
        AddSimpleTaskIterationsBlocks(mainBlock, iterationAssignments);
      }
    }
    
    /// <summary>
    /// Получить предметное отображение простой задачи.
    /// </summary>
    /// <param name="task">Простая задача.</param>
    /// <returns>Предметное отображение простой задачи.</returns>
    private static Sungero.Core.StateBlock GetSimpleTaskMainBlock(ISimpleTask task)
    {
      var stateView = StateView.Create();
      var block = stateView.AddBlock();
      if (task == null)
        return block;
      
      block.Entity = task;
      var inWork = task.Status == Workflow.Task.Status.InProcess || task.Status == Workflow.Task.Status.UnderReview;
      block.IsExpanded = inWork;
      block.AssignIcon(StateBlockIconType.OfEntity, StateBlockIconSize.Large);
      
      // Заголовок. Тема.
      block.AddLabel(string.Format("{0}. {1}", OfficialDocuments.Resources.StateViewTask, task.Subject), Functions.Module.CreateHeaderStyle());
      
      // Срок.
      var hasDeadline = task.MaxDeadline.HasValue;
      var deadline = hasDeadline ? Functions.Module.ToShortDateShortTime(task.MaxDeadline.Value.ToUserTime()) : OfficialDocuments.Resources.StateViewWithoutTerm;
      block.AddLineBreak();
      block.AddLabel(string.Format("{0}: {1}", OfficialDocuments.Resources.StateViewFinalDeadline, deadline), Functions.Module.CreatePerformerDeadlineStyle());
      
      // Текст задачи.
      var taskText = Functions.Module.GetTaskUserComment(task, string.Empty);
      if (!string.IsNullOrWhiteSpace(taskText))
      {
        block.AddLineBreak();
        block.AddLabel(Constants.Module.SeparatorText, Docflow.PublicFunctions.Module.CreateSeparatorStyle());
        block.AddLineBreak();
        block.AddEmptyLine(Constants.Module.EmptyLineMargin);
        
        // Форматирование текста задачи.
        block.AddLabel(taskText);
      }
      
      // Статус.
      var status = Workflow.SimpleTasks.Info.Properties.Status.GetLocalizedValue(task.Status);
      if (!string.IsNullOrEmpty(status))
        Functions.Module.AddInfoToRightContent(block, status);
      
      // Задержка.
      if (hasDeadline && inWork)
        AddDeadlineHeaderToRight(block, task.MaxDeadline.Value, Users.Current);
      
      return block;
    }
    
    private static void AddSimpleTaskIterationsBlocks(StateBlock mainBlock, List<IAssignment> assignments)
    {
      var statusGroups = assignments.OrderByDescending(a => a.Status == Workflow.AssignmentBase.Status.Completed).GroupBy(a => a.Status);
      foreach (var statusGroup in statusGroups)
      {
        var deadlineGroups = statusGroup.OrderBy(a => a.Deadline).GroupBy(a => a.Deadline);
        foreach (var deadlineGroup in deadlineGroups)
        {
          var textGroups = deadlineGroup.OrderBy(a => a.Modified).GroupBy(a => a.ActiveText);
          foreach (var textGroup in textGroups)
          {
            var assignmentBlocks = GetSimpleAssignmentsView(textGroup.ToList()).Blocks;
            if (assignmentBlocks.Any())
              foreach (var block in assignmentBlocks)
                mainBlock.AddChildBlock(block);
          }
        }
      }
    }
    
    /// <summary>
    /// Получить предметное отображение группы простых заданий.
    /// </summary>
    /// <param name="assignments">Простые задания.</param>
    /// <returns>Предметное отображение простого задания.</returns>
    private static Sungero.Core.StateView GetSimpleAssignmentsView(List<IAssignment> assignments)
    {
      var stateView = StateView.Create();
      if (!assignments.Any())
        return stateView;

      // Т.к. задания в пачке должны быть с одинаковым статусом, одинаковым дедлайном - вытаскиваем первый элемент для удобной работы.
      var assignment = assignments.First();
      
      var block = stateView.AddBlock();
      if (assignments.Count == 1)
        block.Entity = assignment;

      // Иконка.
      block.AssignIcon(ApprovalRuleBases.Resources.Assignment, StateBlockIconSize.Large);
      if (assignments.All(a => a.Status == Workflow.AssignmentBase.Status.Completed))
      {
        block.AssignIcon(ApprovalTasks.Resources.Completed, StateBlockIconSize.Large);
      }
      else if (assignments.All(a => a.Status == Workflow.AssignmentBase.Status.Aborted || a.Status == Workflow.AssignmentBase.Status.Suspended))
      {
        block.AssignIcon(StateBlockIconType.Abort, StateBlockIconSize.Large);
      }
      
      // Заголовок.
      var header = ReviewAssignments.Is(assignment) ? OfficialDocuments.Resources.StateViewAssignmentForReview : OfficialDocuments.Resources.StateViewAssignment;
      block.AddLabel(header, Functions.Module.CreateHeaderStyle());
      
      // Кому.
      block.AddLineBreak();
      var performers = assignments.Where(a => Employees.Is(a.Performer)).Select(a => Employees.As(a.Performer)).ToList();
      block.AddLabel(string.Format("{0}: {1} ", OfficialDocuments.Resources.StateViewTo, GetPerformersInText(performers)), Functions.Module.CreatePerformerDeadlineStyle());
      
      // Срок.
      var deadline = assignment.Deadline.HasValue ?
        Functions.Module.ToShortDateShortTime(assignment.Deadline.Value.ToUserTime()) :
        OfficialDocuments.Resources.StateViewWithoutTerm;
      block.AddLabel(string.Format("{0}: {1}", OfficialDocuments.Resources.StateViewDeadline, deadline), Functions.Module.CreatePerformerDeadlineStyle());
      
      // Результат выполнения.
      var activeText = Functions.Module.GetAssignmentUserComment(assignment);
      if (!string.IsNullOrWhiteSpace(activeText))
      {
        block.AddLineBreak();
        block.AddLabel(Constants.Module.SeparatorText, Docflow.PublicFunctions.Module.CreateSeparatorStyle());
        block.AddLineBreak();
        block.AddEmptyLine(Constants.Module.EmptyLineMargin);
        
        block.AddLabel(activeText);
      }
      
      // Статус.
      var assignmentStatus = Workflow.SimpleAssignments.Info.Properties.Status.GetLocalizedValue(assignment.Status);
      if (assignment.Status == Workflow.AssignmentBase.Status.InProcess && assignment.IsRead == false)
      {
        assignmentStatus = Docflow.ApprovalTasks.Resources.StateViewUnRead;
      }
      else if (assignment.Status == Workflow.AssignmentBase.Status.Aborted)
      {
        assignmentStatus = Docflow.ApprovalTasks.Resources.StateViewAborted;
      }
      
      if (!string.IsNullOrEmpty(assignmentStatus))
        Functions.Module.AddInfoToRightContent(block, assignmentStatus);
      
      // Задержка.
      if (assignment.Deadline.HasValue && assignment.Status == Workflow.AssignmentBase.Status.InProcess)
        AddDeadlineHeaderToRight(block, assignment.Deadline.Value, assignment.Performer);
      
      return stateView;
    }
    
    /// <summary>
    /// Сформировать текстовый список исполнителей заданий.
    /// </summary>
    /// <param name="employees">Сотрудники.</param>
    /// <returns>Строка в формате "Ардо Н.А., Соболева Н.Н. и еще 2 сотрудника.".</returns>
    [Public]
    public static string GetPerformersInText(List<IEmployee> employees)
    {
      var employeesCount = employees.Count();
      var maxDisplayedNumberCount = 5;
      var minHiddenNumberCount = 3;
      var displayedValuesCount = employeesCount;
      if (employeesCount >= (maxDisplayedNumberCount + minHiddenNumberCount))
      {
        displayedValuesCount = maxDisplayedNumberCount;
      }
      else if (employeesCount > maxDisplayedNumberCount)
      {
        displayedValuesCount = employeesCount - minHiddenNumberCount;
      }
      
      var employeesText = string.Join(", ", employees.Select(p => Company.PublicFunctions.Employee.GetShortName(p, false)).ToArray(), 0, displayedValuesCount);
      var hiddenSkipedNumberCount = employeesCount - displayedValuesCount;
      if (hiddenSkipedNumberCount > 0)
      {
        var numberLabel = Functions.Module.GetNumberDeclination(hiddenSkipedNumberCount,
                                                                Resources.StateViewEmployee,
                                                                Resources.StateViewEmployeeGenetive,
                                                                Resources.StateViewEmployeePlural);
        employeesText += OfficialDocuments.Resources.StateViewAndFormat(hiddenSkipedNumberCount, numberLabel);
      }
      
      return employeesText;
    }
    
    #endregion
    
    #region Конвертеры

    /// <summary>
    /// Получить ФИО сотрудника для шаблона документа.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    /// <returns>ФИО сотрудника.</returns>
    [Sungero.Core.Converter("FullName")]
    public static PersonFullName FullName(IEmployee employee)
    {
      return FullName(employee.Person);
    }
    
    /// <summary>
    /// Получить ФИО контакта для шаблона документа.
    /// </summary>
    /// <param name="contact">Контакт.</param>
    /// <returns>ФИО контакта.</returns>
    [Sungero.Core.Converter("FullName")]
    public static PersonFullName FullName(IContact contact)
    {
      if (contact.Person != null)
        return FullName(contact.Person);
      
      PersonFullName contactPersonalData;
      return PersonFullName.TryParse(contact.Name, out contactPersonalData) ?
        contactPersonalData :
        PersonFullName.CreateUndefined(contact.Name);
    }
    
    /// <summary>
    /// Получить ФИО персоны для шаблона документа.
    /// </summary>
    /// <param name="person">Персона.</param>
    /// <returns>ФИО персоны.</returns>
    [Sungero.Core.Converter("FullName")]
    public static PersonFullName FullName(IPerson person)
    {
      person = Sungero.Parties.People.As(person);
      if (person == null)
        return null;
      return PersonFullName.Create(person.LastName, person.FirstName, person.MiddleName);
    }
    
    /// <summary>
    /// Получить фамилию и инициалы сотрудника для шаблона документа.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    /// <returns>Фамилия и инициалы сотрудника.</returns>
    [Sungero.Core.Converter("LastNameAndInitials")]
    public static PersonFullName LastNameAndInitials(IEmployee employee)
    {
      return LastNameAndInitials(employee.Person);
    }
    
    /// <summary>
    /// Получить фамилию и инициалы контакта для шаблона документа.
    /// </summary>
    /// <param name="contact">Контакт.</param>
    /// <returns>Фамилия и инициалы контакта.</returns>
    [Sungero.Core.Converter("LastNameAndInitials")]
    public static PersonFullName LastNameAndInitials(IContact contact)
    {
      if (contact.Person != null)
        return LastNameAndInitials(contact.Person);
      
      PersonFullName contactPersonalData;
      return PersonFullName.TryParse(contact.Name, out contactPersonalData) ?
        PersonFullName.Create(contactPersonalData.LastName, contactPersonalData.FirstName, contactPersonalData.MiddleName, PersonFullNameDisplayFormat.LastNameAndInitials) :
        PersonFullName.CreateUndefined(contact.Name);
    }
    
    /// <summary>
    /// Получить фамилию и инициалы персоны для шаблона документа.
    /// </summary>
    /// <param name="counterparty">Персона.</param>
    /// <returns>Фамилия и инициалы персоны.</returns>
    [Sungero.Core.Converter("LastNameAndInitials")]
    public static PersonFullName LastNameAndInitials(IPerson counterparty)
    {
      var person = Sungero.Parties.People.As(counterparty);
      if (person != null)
        return PersonFullName.Create(person.LastName, person.FirstName, person.MiddleName, PersonFullNameDisplayFormat.LastNameAndInitials);
      return null;
    }
    
    /// <summary>
    /// Получить инициалы и фамилию сотрудника для шаблона документа.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    /// <returns>Инициалы и фамилия сотрудника.</returns>
    [Sungero.Core.Converter("InitialsAndLastName")]
    public static PersonFullName InitialsAndLastName(IEmployee employee)
    {
      return InitialsAndLastName(employee.Person);
    }
    
    /// <summary>
    /// Получить инициалы и фамилию контакта для шаблона документа.
    /// </summary>
    /// <param name="contact">Контакт.</param>
    /// <returns>Инициалы и фамилия контакта.</returns>
    [Sungero.Core.Converter("InitialsAndLastName")]
    public static PersonFullName InitialsAndLastName(IContact contact)
    {
      if (contact.Person != null)
        return InitialsAndLastName(contact.Person);
      
      PersonFullName contactPersonalData;
      return PersonFullName.TryParse(contact.Name, out contactPersonalData) ?
        PersonFullName.Create(contactPersonalData.LastName, contactPersonalData.FirstName, contactPersonalData.MiddleName, PersonFullNameDisplayFormat.InitialsAndLastName) :
        PersonFullName.CreateUndefined(contact.Name);
    }
     
    /// <summary>
    /// Получить инициалы и фамилию персоны для шаблона документа.
    /// </summary>
    /// <param name="person">Персона.</param>
    /// <returns>Инициалы и фамилия персоны.</returns>
    [Sungero.Core.Converter("InitialsAndLastName")]
    public static PersonFullName InitialsAndLastName(IPerson person)
    {
      person = Sungero.Parties.People.As(person);
      if (person == null)
        return null;
      return PersonFullName.Create(person.LastName, person.FirstName, person.MiddleName, PersonFullNameDisplayFormat.InitialsAndLastName);
    }
    
    /// <summary>
    /// Получить перечень приложений для шаблона документа.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Перечень приложений.</returns>
    [Sungero.Core.Converter("Addenda")]
    public static string Addenda(IOfficialDocument document)
    {
      var documentAddenda = document.Relations.GetRelated(Constants.Module.AddendumRelationName).Select(doc => doc.DisplayValue).ToList();
      var result = string.Empty;
      for (var i = 1; i <= documentAddenda.Count; i++)
        result += string.Format("{0}. {1}{2}", i, documentAddenda[i - 1], Environment.NewLine);
      
      return result;
    }
    
    /// <summary>
    /// Получить отметку об исполнителе для шаблона документа.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Отметка об исполнителе.</returns>
    [Sungero.Core.Converter("PerformerNotes")]
    public static string PerformerNotes(IOfficialDocument document)
    {
      // Исключить дублирование исполнителя (подготовил) и подписывающего в шаблоне.
      if (!Equals(document.PreparedBy, document.OurSignatory))
      {
        if (document.PreparedBy.Phone == null)
          return InitialsAndLastName(document.PreparedBy.Person).ToString();
        else
          return string.Format("{0} {1} {2}",
                               InitialsAndLastName(document.PreparedBy.Person).ToString(),
                               Environment.NewLine, document.PreparedBy.Phone);
      }
      
      return string.Empty;
      
    }
    
    #endregion
    
    #region Работа со связями
    
    /// <summary>
    /// Получить тип связи по наименованию.
    /// </summary>
    /// <param name="relationName">Наименование типа связи.</param>
    /// <returns>Тип связи.</returns>
    [Remote]
    public static Sungero.CoreEntities.IRelationType GetRelationTypeByName(string relationName)
    {
      return Sungero.CoreEntities.RelationTypes.GetAll()
        .FirstOrDefault(x => x.Name == relationName);
    }
    
    /// <summary>
    /// Получить связанные документы по типу связи.
    /// </summary>
    /// <param name="document">Документ, для которого получаются связанные документы.</param>
    /// <param name="relationTypeName">Наименование типа связи.</param>
    /// <param name="withVersion">Учитывать только документы с версиями.</param>
    /// <returns>Связанные документы.</returns>
    [Remote]
    public static List<IOfficialDocument> GetRelatedDocumentsByRelationType(IOfficialDocument document, string relationTypeName, bool withVersion)
    {
      if (string.IsNullOrWhiteSpace(relationTypeName))
        return new List<IOfficialDocument>();
      
      var relationType = GetRelationTypeByName(relationTypeName);
      
      if (relationType == null)
        return new List<IOfficialDocument>();
      
      // Прямая связь. Текущий - Source, связанный - Target.
      var relations = Sungero.Content.DocumentRelations.GetAll()
        .Where(x => Equals(x.RelationType, relationType) &&
               Equals(x.Source, document));
      var documents = relations
        .Where(x => !withVersion || x.Target.HasVersions)
        .Where(x => OfficialDocuments.Is(x.Target))
        .Select(x => x.Target)
        .Cast<IOfficialDocument>().ToList();
      
      if (relationType.HasDirection == true)
        return documents;
      
      // Обратная связь. Текущий - Target, связанный - Source.
      relations = Sungero.Content.DocumentRelations.GetAll()
        .Where(x => Equals(x.RelationType, relationType) &&
               Equals(x.Target, document));
      documents.AddRange(relations
                         .Where(x => !withVersion || x.Source.HasVersions)
                         .Where(x => OfficialDocuments.Is(x.Source))
                         .Select(x => x.Source)
                         .Cast<IOfficialDocument>().ToList());
      
      return documents;
    }
    
    #endregion
    
    /// <summary>
    /// Получить все данные для отображения диалога регистрации.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="operation">Операция.</param>
    /// <returns>Параметры диалога.</returns>
    [Remote(IsPure = true)]
    public static Structures.OfficialDocument.DialogParams GetRegistrationDialogParams(IOfficialDocument document, Enumeration operation)
    {
      var leadDocumentId = Functions.OfficialDocument.GetLeadDocumentId(document);
      var leadDocumentNumber = Functions.OfficialDocument.GetLeadDocumentNumber(document);
      var numberValidationDisabled = Functions.OfficialDocument.IsNumberValidationDisabled(document);
      var departmentId = document.Department != null ? document.Department.Id : 0;
      var departmentCode = document.Department != null ? document.Department.Code : string.Empty;
      var businessUnitId = document.BusinessUnit != null ? document.BusinessUnit.Id : 0;
      var businessUnitCode = document.BusinessUnit != null ? document.BusinessUnit.Code : string.Empty;
      var docKindCode = document.DocumentKind != null ? document.DocumentKind.Code : string.Empty;
      var caseFileIndex = document.CaseFile != null ? document.CaseFile.Index : string.Empty;
      var isClerk = document.AccessRights.CanRegister();
      var counterpartyCode = Functions.OfficialDocument.GetCounterpartyCode(document);
      var currentRegistrationDate = document.RegistrationDate ?? Calendar.UserToday;
      
      var registers = Functions.OfficialDocument.GetDocumentRegistersByDocument(document, operation);
      var defaultDocumentRegister = Functions.DocumentRegister.GetDefaultDocRegister(document, registers, operation);
      string nextNumber = string.Empty;
      if (defaultDocumentRegister != null)
        nextNumber = Functions.DocumentRegister.GetNextNumber(defaultDocumentRegister, currentRegistrationDate, leadDocumentId, document,
                                                              leadDocumentNumber, departmentId, businessUnitId, caseFileIndex, docKindCode,
                                                              Constants.OfficialDocument.DefaultIndexLeadingSymbol);
      
      return Structures.OfficialDocument.DialogParams.Create(registers, operation, defaultDocumentRegister,
                                                             document.RegistrationNumber, currentRegistrationDate, nextNumber,
                                                             leadDocumentId, leadDocumentNumber, numberValidationDisabled,
                                                             departmentId, departmentCode, businessUnitCode, businessUnitId,
                                                             caseFileIndex, docKindCode, counterpartyCode, isClerk);
    }
    
    /// <summary>
    /// Признак того, что формат номера не надо валидировать.
    /// </summary>
    /// <returns>True, если формат номера неважен.</returns>
    [Remote(IsPure = true)]
    public virtual bool IsNumberValidationDisabled()
    {
      // Для всех контрактных документов валидация отключена (в т.ч. - для автонумеруемых).
      if (_obj.DocumentKind != null && _obj.DocumentKind.DocumentFlow == Docflow.DocumentKind.DocumentFlow.Contracts)
        return true;
      
      // Для автонумеруемых неконтрактных документов валидация включена.
      if (_obj.DocumentKind != null && _obj.DocumentKind.AutoNumbering.Value)
        return false;
      
      // Для неавтонумеруемых финансовых документов валидация отключена.
      return AccountingDocumentBases.Is(_obj);
    }
    
    /// <summary>
    /// Сформировать текстовку для местонахождения.
    /// </summary>
    /// <returns>Местонахождение.</returns>
    public virtual string GetLocationState()
    {
      var tracking = _obj.Tracking.Where(l => !l.ReturnDate.HasValue && l.DeliveredTo != null).OrderByDescending(l => l.DeliveryDate);
      var originalTracking = tracking.Where(l => (l.IsOriginal ?? false) && l.Action == Docflow.OfficialDocumentTracking.Action.Delivery);
      var copyTracking = tracking.Where(l => !(l.IsOriginal ?? false) && l.Action == Docflow.OfficialDocumentTracking.Action.Delivery && l.ReturnDeadline.HasValue);

      var originalTrackingAtContractor = tracking.Where(l => (l.IsOriginal ?? false) &&
                                                        (l.Action == Docflow.OfficialDocumentTracking.Action.Endorsement ||
                                                         l.Action == Docflow.OfficialDocumentTracking.Action.Sending) &&
                                                        l.ReturnDeadline.HasValue &&
                                                        l.ExternalLinkId == null);
      
      var copyTrackingAtContractor = tracking.Where(l => !(l.IsOriginal ?? false) &&
                                                    (l.Action == Docflow.OfficialDocumentTracking.Action.Endorsement ||
                                                     l.Action == Docflow.OfficialDocumentTracking.Action.Sending) &&
                                                    l.ReturnDeadline.HasValue &&
                                                    l.ExternalLinkId == null);
      
      var canShowExchange = _obj.Tracking.Any(t => t.ExternalLinkId != null && t.ReturnResult == null && t.ReturnDeadline != null) ||
        Sungero.Exchange.ExchangeDocumentInfos.GetAll(x => Equals(x.Document, _obj)).Any();
      
      // Сформировать в культуре тенанта.
      using (Core.CultureInfoExtensions.SwitchTo(TenantInfo.Culture))
      {
        var trackingState = string.Empty;
        if (originalTracking.Any() || copyTracking.Any() || originalTrackingAtContractor.Any() || copyTrackingAtContractor.Any()
            || _obj.State.Properties.ExchangeState.IsChanged || _obj.State.Properties.Tracking.IsChanged || canShowExchange)
        {
          var originals = string.Join("; \n", originalTracking
                                      .Select(l => Docflow.Resources.OriginalDocumentLocatedInFormat(Company.PublicFunctions.Employee.GetShortName(l.DeliveredTo, DeclensionCase.Genitive, false))));
          
          var copies = string.Join("; \n", copyTracking
                                   .Select(l => Docflow.Resources.CopyDocumentLocatedInFormat(Company.PublicFunctions.Employee.GetShortName(l.DeliveredTo, DeclensionCase.Genitive, false))));
          
          var originalsAtContractor = string.Join("; \n", originalTrackingAtContractor
                                                  .Select(l => Docflow.Resources.OriginalDocumentLocatedInFormat(Docflow.Resources.AtContractorTrackingShowing)));
          
          var exchangeLocation = Functions.OfficialDocument.GetExchangeLocation(_obj);
          
          var copiesAtContractor = string.Join("; \n", copyTrackingAtContractor
                                               .Select(l => Docflow.Resources.CopyDocumentLocatedInFormat(Docflow.Resources.AtContractorTrackingShowing)));
          
          trackingState = originals;
          
          if (!string.IsNullOrEmpty(originalsAtContractor))
            trackingState = string.Join(string.IsNullOrEmpty(trackingState) ? string.Empty : "; \n", trackingState, originalsAtContractor);
          
          if (!string.IsNullOrEmpty(exchangeLocation))
            trackingState = string.Join(string.IsNullOrEmpty(trackingState) ? string.Empty : "; \n", trackingState, exchangeLocation);
          
          if (!string.IsNullOrEmpty(copies))
            trackingState = string.Join(string.IsNullOrEmpty(trackingState) ? string.Empty : "; \n", trackingState, copies);
          
          if (!string.IsNullOrEmpty(copiesAtContractor))
            trackingState = string.Join(string.IsNullOrEmpty(trackingState) ? string.Empty : "; \n", trackingState, copiesAtContractor);
        }
        
        if (string.IsNullOrEmpty(trackingState) && _obj.ExchangeState == null)
        {
          if (_obj.CaseFile != null)
            trackingState = Sungero.Docflow.Resources.InFilelistFormat(_obj.CaseFile.Index, _obj.CaseFile.Title);
          else if (_obj.RegistrationState == Docflow.OfficialDocument.RegistrationState.Registered && _obj.DocumentRegister != null && _obj.DocumentRegister.RegistrationGroup != null)
            trackingState = Sungero.Docflow.Resources.InRegistrationGroupOfFormat(_obj.DocumentRegister.RegistrationGroup);
        }
        return trackingState;
      }
    }
    
    /// <summary>
    /// Проверить, изменялась ли только версия.
    /// </summary>
    /// <returns>Признак измененности.</returns>
    public bool IsOnlyVersionChanged()
    {
      // Свойства, которые меняются при изменении тела.
      var versionChangingProperties = new List<IPropertyStateBase>()
      {
        _obj.State.Properties.Modified,
        _obj.State.Properties.AssociatedApplication,
        _obj.State.Properties.Versions
      };
      
      var onlyBodyPropertiesChanged = !_obj.State.Properties
        .Where(p => (p as IPropertyState).IsChanged && !versionChangingProperties.Contains(p)).Any();

      // У документов, полученных из сервиса обмена, могло измениться местонахождение.
      var locationChanged = _obj.LocationState != Functions.OfficialDocument.GetLocationState(_obj);
      
      return onlyBodyPropertiesChanged && !locationChanged;
    }
    
    private List<string> GetOldVersionsExchangeLocation()
    {
      var result = new List<string>();
      var infos = Sungero.Exchange.ExchangeDocumentInfos.GetAll(x => Equals(x.Document, _obj)).ToList();
      foreach (var version in _obj.Versions.Where(v => !Equals(v, _obj.LastVersion)).OrderByDescending(v => v.Number))
      {
        var exchangeDocumentInfo = infos.FirstOrDefault(x => x.VersionId == version.Id);
        if (exchangeDocumentInfo == null)
          continue;
        
        var exchangeService = ExchangeCore.PublicFunctions.BoxBase.GetExchangeService(exchangeDocumentInfo.Box).Name;
        var isIncoming = exchangeDocumentInfo.MessageType == Sungero.Exchange.ExchangeDocumentInfo.MessageType.Incoming;
        var prefix = isIncoming ? OfficialDocuments.Resources.DocumentIsReceivedFromFormat(exchangeService) : OfficialDocuments.Resources.DocumentIsSentToFormat(exchangeService);
        var detailed = this.GetExchangeState(exchangeDocumentInfo);
        
        if (!string.IsNullOrEmpty(detailed))
          result.Add(string.Format("{0}. {1}", prefix, OfficialDocuments.Resources.LocationVersionFormat(detailed, version.Number)));
        else
          result.Add(string.Format("{0}{1}", prefix, OfficialDocuments.Resources.LocationVersionFormat(string.Empty, version.Number)));
        
        if (exchangeDocumentInfo.ExchangeState == ExchangeState.Signed)
          break;
      }
      
      return result;
    }
    
    private string GetExchangeState(Exchange.IExchangeDocumentInfo exchangeDocumentInfo)
    {
      var result = string.Empty;
      if (exchangeDocumentInfo == null || exchangeDocumentInfo.ExchangeState == null)
        return result;
      if (exchangeDocumentInfo.ExchangeState == ExchangeState.Signed || exchangeDocumentInfo.ExchangeState == ExchangeState.Obsolete ||
          exchangeDocumentInfo.ExchangeState == ExchangeState.Rejected || exchangeDocumentInfo.ExchangeState == ExchangeState.Terminated)
      {
        // Подписан, или аннулирован, или отказано в подписании, или отозван.
        result = _obj.Info.Properties.ExchangeState.GetLocalizedValue(exchangeDocumentInfo.ExchangeState);
      }
      else if (exchangeDocumentInfo.ExchangeState == ExchangeState.SignAwaited)
      {
        // Ожидается подписание контрагентом.
        result = OfficialDocuments.Resources.ExchangeStateSignAwaited;
      }
      else if (exchangeDocumentInfo.ExchangeState == ExchangeState.SignRequired)
      {
        // Требуется подписание.
        result = OfficialDocuments.Resources.ExchangeStateSignRequired;
      }
      
      return result;
    }
    
    /// <summary>
    /// Получить местонахождение документа в сервисе обмена.
    /// </summary>
    /// <returns>Местонахождение документа в сервисе обмена. Пусто - если документ не ходил через сервис обмена.</returns>
    public string GetExchangeLocation()
    {
      if (_obj.LastVersion == null)
        return string.Empty;
      
      var result = string.Empty;
      var lastVersionId = _obj.LastVersion.Id;
      var accountDocument = AccountingDocumentBases.As(_obj);
      var isFormalized = accountDocument != null && accountDocument.IsFormalized == true;
      
      // У документов с титулом покупателя инфошка только на титул продавца.
      if (isFormalized && accountDocument.BuyerTitleId == lastVersionId)
        lastVersionId = accountDocument.SellerTitleId.Value;

      var exchangeDocumentInfo = Sungero.Exchange.PublicFunctions.ExchangeDocumentInfo.Remote.GetExDocumentInfoFromVersion(_obj, lastVersionId);
      var lastVersionIsSigned = exchangeDocumentInfo != null && exchangeDocumentInfo.ExchangeState == ExchangeState.Signed;
      if (_obj.Versions.Count > 1 && !lastVersionIsSigned && !isFormalized)
      {
        var oldVersions = this.GetOldVersionsExchangeLocation();
        if (oldVersions.Any())
          result = string.Join("; \n", oldVersions);
      }

      if (exchangeDocumentInfo == null)
        return result;
      
      var exchangeService = ExchangeCore.PublicFunctions.BoxBase.GetExchangeService(exchangeDocumentInfo.Box).Name;
      var isIncoming = exchangeDocumentInfo.MessageType == Sungero.Exchange.ExchangeDocumentInfo.MessageType.Incoming;
      var prefix = isIncoming ? OfficialDocuments.Resources.DocumentIsReceivedFromFormat(exchangeService) : OfficialDocuments.Resources.DocumentIsSentToFormat(exchangeService);
      var detailed = this.GetExchangeState(exchangeDocumentInfo);
      
      var main = string.Format("{0}. {1}", prefix, detailed);
      if (isIncoming && exchangeDocumentInfo.InvoiceState == Exchange.ExchangeDocumentInfo.InvoiceState.Rejected)
        main = string.Format("{0}. {1}", main.TrimEnd(' ', '.'), OfficialDocuments.Resources.LocationIncomingInvoiceRejected);
      if (!isIncoming && exchangeDocumentInfo.InvoiceState == Exchange.ExchangeDocumentInfo.InvoiceState.Rejected)
        main = string.Format("{0}. {1}", main.TrimEnd(' ', '.'), OfficialDocuments.Resources.LocationOutgoingInvoiceRejected);
      if (!string.IsNullOrEmpty(result))
        main = OfficialDocuments.Resources.LocationVersionFormat(main, _obj.Versions.Where(v => v.Id == lastVersionId).Single().Number);
      
      return string.Join(string.IsNullOrEmpty(result) ? string.Empty : "; \n", main, result);
    }
    
    /// <summary>
    /// Получить id задач, в которых документ вложен в обязательные группы.
    /// </summary>
    /// <returns>Список id задач.</returns>
    [Remote]
    public virtual List<int> GetTaskIdsWhereDocumentInRequredGroup()
    {
      var ids = new List<int>();
      using (var session = new Domain.Session())
      {
        var innerSession = (Sungero.Domain.ISession)session
          .GetType()
          .GetField("InnerSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
          .GetValue(session);
        
        var docGuid = _obj.GetEntityMetadata().GetOriginal().NameGuid;
        var tasks = innerSession.GetAll<ITask>()
          .Where(t => t.Status != Workflow.Task.Status.Draft)
          .Where(t => t.AttachmentDetails
                 .Any(att => att.AttachmentId == _obj.Id && att.EntityTypeGuid == docGuid))
          .ToList();
        var attachmentDetails = tasks
          .SelectMany(t => t.AttachmentDetails)
          .Where(ad => ad.AttachmentId == _obj.Id && ad.EntityTypeGuid == docGuid)
          .ToList();
        
        foreach (var task in tasks)
        {
          var groups = attachmentDetails.Any(x => x.Group.IsRequired);
          
          // Другие задачи, где документы вложены в основную, но необязательную группу и их удаление приведет к нарушению процесса.
          var otherTasks = Functions.Module.GetServerEntityFunctionResult(task, "DocumentInRequredGroup", new List<object> { _obj });
          if (groups || (otherTasks != null && (bool)otherTasks))
            ids.Add(task.Id);
        }
      }
      return ids;
    }
    
    /// <summary>
    /// Проверить наличие согласующих или утверждающих подписей на документе.
    /// </summary>
    /// <returns>True, если есть хоть одна подпись для отображения в отчете.</returns>
    [Remote(IsPure = true)]
    public bool HasSignatureForApprovalSheetReport()
    {
      var setting = Docflow.PublicFunctions.PersonalSetting.GetPersonalSettings(null);
      var showNotApproveSign = setting != null ? setting.ShowNotApproveSign == true : false;
      
      return Signatures.Get(_obj).Any(s => (showNotApproveSign || s.SignatureType != SignatureType.NotEndorsing) && s.IsExternal != true);
    }
    
    /// <summary>
    /// Получить все задачи на ознакомление.
    /// </summary>
    /// <returns>Задачи на ознакомление с документом.</returns>
    [Public, Remote(IsPure = true)]
    public List<RecordManagement.IAcquaintanceTask> GetAcquaintanceTasks()
    {
      var tasks = RecordManagement.AcquaintanceTasks.GetAll()
        .Where(x => x.Status == Workflow.Task.Status.InProcess ||
               x.Status == Workflow.Task.Status.Suspended ||
               x.Status == Workflow.Task.Status.Completed)
        .Where(x => x.AttachmentDetails.Any(d => d.GroupId.ToString() == "19c1e8c9-e896-4d93-a1e8-4e22b932c1ce" &&
                                            d.AttachmentId == _obj.Id))
        .ToList();
      
      return tasks;
    }
    
    /// <summary>
    /// Получить руководителя НОР документа или сотрудника.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    /// <param name="document">Документ. По нему определяется НОР. Если не указан, будет выбрана НОР сотрудника.</param>
    /// <returns>Подписывающий.</returns>
    public static IEmployee GetBusinessUnitCEO(IEmployee employee, IOfficialDocument document)
    {
      var businessUnit = document != null ? document.BusinessUnit : Company.PublicFunctions.BusinessUnit.Remote.GetBusinessUnit(employee);
      return businessUnit != null ? businessUnit.CEO : null;
    }
    
    /// <summary>
    /// Возвращает список подписывающих по правилу.
    /// </summary>
    /// <returns>Список тех, кто имеет право подписи.</returns>
    [Remote(IsPure = true)]
    public virtual List<Sungero.Docflow.Structures.SignatureSetting.Signatory> GetSignatories()
    {
      var signatories = new List<Sungero.Docflow.Structures.SignatureSetting.Signatory>();
      
      if (_obj == null)
        return signatories;

      var settings = Functions.OfficialDocument.GetSignatureSettings(_obj);
      foreach (var setting in settings)
      {
        var priority = setting.Priority.Value;
        if (Groups.Is(setting.Recipient))
        {
          var employeesId = new List<int>();
          if (Constants.OfficialDocument.AllUsersSid == setting.Recipient.Sid)
            employeesId = Employees.GetAll().Select(x => x.Id).ToList();
          else
            employeesId.AddRange(Functions.OfficialDocument.GetAllRecipientMembersIdsInGroup(Groups.As(setting.Recipient).Id));

          foreach (var employeeId in employeesId)
          {
            var signature = Docflow.Structures.SignatureSetting.Signatory.Create();
            signature.EmployeeId = employeeId;
            signature.Priority = priority;
            signatories.Add(signature);
          }
        }
        else if (Employees.Is(setting.Recipient))
        {
          var signature = Docflow.Structures.SignatureSetting.Signatory.Create();
          signature.EmployeeId = setting.Recipient.Id;
          signature.Priority = priority;
          signatories.Add(signature);
        }
      }

      return signatories;
    }
    
    /// <summary>
    /// Получить список Ид участников группы.
    /// </summary>
    /// <param name="groupId">Ид группы.</param>
    /// <returns>Список Ид участников.</returns>
    [Public]
    public static List<int> GetAllRecipientMembersIdsInGroup(int groupId)
    {
      var employeesId = new List<int>();
      var commandText = string.Format(Queries.Module.GetAllRecipientMembers, groupId);
      using (var command = SQL.GetCurrentConnection().CreateCommand())
      {
        command.CommandText = commandText;
        using (var reader = command.ExecuteReader())
        {
          while (reader.Read())
            employeesId.Add(reader.GetInt32(0));
        }
      }
      return employeesId;
    }
    
    /// <summary>
    /// Возвращает список подписывающих по правилу.
    /// </summary>
    /// <returns>Список Ид тех, кто имеет право подписи.</returns>
    [Public, Remote(IsPure = true)]
    public virtual List<int> GetEmployeeSignatories()
    {
      var signatories = this.GetSignatories();
      return signatories.Select(s => s.EmployeeId).Distinct().ToList();
    }

    /// <summary>
    /// Получить права подписания документов.
    /// </summary>
    /// <returns>Список подходящих правил.</returns>
    [Public, Remote(IsPure = true)]
    public virtual List<ISignatureSetting> GetSignatureSettings()
    {
      if (_obj.DocumentKind == null)
        return new List<ISignatureSetting>();
      
      var docflow = _obj.DocumentKind.DocumentFlow;
      var businessUnits = new List<IBusinessUnit>() { };
      if (_obj.BusinessUnit != null)
        businessUnits.Add(_obj.BusinessUnit);
      var kinds = new List<IDocumentKind>() { };
      if (_obj.DocumentKind != null)
        kinds.Add(_obj.DocumentKind);
      
      var settings = Functions.SignatureSetting.GetSignatureSettings(businessUnits, kinds)
        .Where(s => s.DocumentFlow == Docflow.SignatureSetting.DocumentFlow.All || s.DocumentFlow == docflow)
        .Where(s => !s.Departments.Any() || s.Departments.Any(d => Equals(d.Department, _obj.Department)) || _obj.Department == null)
        .ToList();
      return settings;
    }
    
    /// <summary>
    /// Получить права подписания документов.
    /// </summary>
    /// <param name="employee">Сотрудник, для которого запрашиваются права.</param>
    /// <returns>Список подходящих правил.</returns>
    [Public, Remote(IsPure = true)]
    public virtual List<ISignatureSetting> GetSignatureSettings(IEmployee employee)
    {
      var result = new List<ISignatureSetting>();
      var documentSettings = Functions.OfficialDocument.GetSignatureSettings(_obj);
      foreach (var setting in documentSettings)
      {
        if (Groups.Is(setting.Recipient) && Groups.GetAllUsersInGroup(Groups.As(setting.Recipient)).Contains(employee))
          result.Add(setting);
        else if (Equals(setting.Recipient, employee))
          result.Add(setting);
      }
      return result;
    }

    /// <summary>
    /// Заполнить статус согласования "Подписан".
    /// </summary>
    [Remote]
    public void SetInternalApprovalStateToSigned()
    {
      if (_obj.InternalApprovalState == InternalApprovalState.Aborted ||
          _obj.InternalApprovalState == InternalApprovalState.Signed)
        return;
      
      if (Equals(_obj.InternalApprovalState, InternalApprovalState.Signed))
        return;
      
      // HACK: если нет прав, то статус будет заполнен независимо от прав доступа.
      if (!_obj.AccessRights.CanUpdate())
      {
        using (var session = new Sungero.Domain.Session())
        {
          Functions.Module.AddFullRightsInSession(session, _obj);
          _obj.InternalApprovalState = InternalApprovalState.Signed;
          _obj.Save();
        }
      }
      else
        _obj.InternalApprovalState = InternalApprovalState.Signed;
    }
    
    /// <summary>
    /// Заполнить подписывающего в карточке документа.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    [Remote]
    public virtual void SetDocumentSignatory(IEmployee employee)
    {
      if (Equals(_obj.OurSignatory, employee))
        return;
      
      // HACK: если нет прав, то подписывающий будет заполнен независимо от прав доступа.
      if (!_obj.AccessRights.CanUpdate())
      {
        using (var session = new Sungero.Domain.Session())
        {
          Functions.Module.AddFullRightsInSession(session, _obj);
          _obj.OurSignatory = employee;
          _obj.Save();
        }
      }
      else
      {
        _obj.OurSignatory = employee;
        _obj.Save();
      }
    }
    
    /// <summary>
    /// Получить задания на возврат по документу.
    /// </summary>
    /// <param name="returnTask">Задача.</param>
    /// <returns>Задания на возврат.</returns>
    [Remote(IsPure = true)]
    public static List<Sungero.Workflow.IAssignment> GetReturnAssignments(Sungero.Workflow.ITask returnTask)
    {
      return GetReturnAssignments(new List<Sungero.Workflow.ITask>() { returnTask });
    }
    
    /// <summary>
    /// Получить задания на возврат по документу.
    /// </summary>
    /// <param name="returnTasks">Задачи.</param>
    /// <returns>Задания на возврат.</returns>
    [Remote(IsPure = true)]
    public static List<Sungero.Workflow.IAssignment> GetReturnAssignments(List<Sungero.Workflow.ITask> returnTasks)
    {
      var assignments = new List<Sungero.Workflow.IAssignment>();
      assignments.AddRange(CheckReturnCheckAssignments.GetAll(a => returnTasks.Contains(a.Task) && a.Status == Workflow.AssignmentBase.Status.InProcess).ToList());
      assignments.AddRange(CheckReturnAssignments.GetAll(a => returnTasks.Contains(a.Task) && a.Status == Workflow.AssignmentBase.Status.InProcess).ToList());
      assignments.AddRange(ApprovalCheckReturnAssignments.GetAll(a => returnTasks.Contains(a.Task) && a.Status == Workflow.AssignmentBase.Status.InProcess).ToList());
      
      return assignments.ToList();
    }
    
    /// <summary>
    /// Возвращает ошибки валидации подписания документа.
    /// </summary>
    /// <param name="checkSignatureSettings">Проверять права подписи.</param>
    /// <returns>Ошибки валидации.</returns>
    [Remote(IsPure = true), Public]
    public virtual List<string> GetApprovalValidationErrors(bool checkSignatureSettings)
    {
      var employee = Users.Current;
      var errors = new List<string>();
      if (!_obj.AccessRights.CanApprove())
        errors.Add(Docflow.Resources.NoAccessRightsToApprove);
      
      var signatories = new List<Docflow.Structures.SignatureSetting.Signatory>();
      if (checkSignatureSettings)
      {
        // Поиск прав подписи документа.
        signatories = Functions.OfficialDocument.GetSignatories(_obj);
        
        if (_obj.AccessRights.CanApprove() && (!signatories.Any() || !signatories.Any(s => Equals(s.EmployeeId, employee.Id))))
          errors.Add(Docflow.Resources.NoRightsToApproveDocument);
      }
      
      // Если документ заблокирован - утвердить его нельзя (т.к. мы должны заполнить два поля при утверждении).
      errors.AddRange(Functions.OfficialDocument.GetDocumentLockErrors(_obj));
      return errors;
    }

    /// <summary>
    /// Возвращает ошибки заблокированности документа.
    /// </summary>
    /// <returns>Ошибки заблокированности документа.</returns>
    public virtual List<string> GetDocumentLockErrors()
    {
      var errors = new List<string>();
      var lockInfo = Locks.GetLockInfo(_obj);
      
      if (_obj.AccessRights.CanApprove() && lockInfo != null && lockInfo.IsLockedByOther)
        errors.Add(lockInfo.LockedMessage);
      
      if (_obj.LastVersion != null)
      {
        var lockInfoVersion = Locks.GetLockInfo(_obj.LastVersion.Body);
        if (_obj.AccessRights.CanApprove() && lockInfoVersion != null && lockInfoVersion.IsLockedByOther)
          errors.Add(lockInfoVersion.LockedMessage);
      }
      return errors;
    }
    
    /// <summary>
    /// Получить операцию по статусу.
    /// </summary>
    /// <param name="state">Статус.</param>
    /// <param name="statePrefix">Префикс.</param>
    /// <param name="isUpdateAction">Признак обновления.</param>
    /// <returns>Операция по статусу.</returns>
    public static Enumeration? GetHistoryOperationByLifeCycleState(Enumeration? state, string statePrefix, bool isUpdateAction)
    {
      if (state != null)
      {
        var stateName = statePrefix + state.ToString();
        if (stateName.Length > Constants.OfficialDocument.Operation.OperationPropertyLength)
          stateName = stateName.Substring(0, Sungero.Docflow.Constants.OfficialDocument.Operation.OperationPropertyLength);
        return new Enumeration(stateName);
      }
      else if (isUpdateAction)
        return new Enumeration(statePrefix + "StateClear");

      return null;
    }
    
    /// <summary>
    /// Получить правила согласования для документа.
    /// </summary>
    /// <returns>Правила согласования, доступные для документа в порядке убывания приоритета.</returns>
    [Remote, Public]
    public virtual List<IApprovalRuleBase> GetApprovalRules()
    {
      return Docflow.PublicFunctions.ApprovalRuleBase.Remote.GetAvailableRulesByDocument(_obj)
        .OrderByDescending(r => r.Priority)
        .ToList();
    }
    
    /// <summary>
    /// Получить вид документа по умолчанию.
    /// </summary>
    /// <returns>Вид документа.</returns>
    [Public]
    public virtual IDocumentKind GetDefaultDocumentKind()
    {
      var availableDocumentKinds = Functions.DocumentKind.GetAvailableDocumentKinds(_obj);
      return availableDocumentKinds.Where(k => k.IsDefault == true).FirstOrDefault();
    }
    
    /// <summary>
    /// Проверка, может ли текущий сотрудник менять поле "Исполнитель".
    /// </summary>
    /// <returns>True, если может.</returns>
    [Remote(IsPure = true)]
    public virtual bool CanChangeAssignee()
    {
      var documentRegister = _obj.DocumentRegister;
      if (_obj.AccessRights.CanRegister() && _obj.DocumentKind != null &&
          _obj.DocumentKind.NumberingType == Docflow.DocumentKind.NumberingType.Registrable &&
          _obj.AccessRights.CanUpdate() && _obj.RegistrationState == RegistrationState.Registered &&
          documentRegister != null && documentRegister.RegistrationGroup != null)
      {
        var employee = Employees.Current;
        return employee != null && (employee.IncludedIn(documentRegister.RegistrationGroup) ||
                                    Equals(employee, documentRegister.RegistrationGroup.ResponsibleEmployee));
      }
      return false;
    }
    
    /// <summary>
    /// Признак того, что необходимо проверять наличие прав подписи на документ у сотрудника, указанного в качестве подписанта с нашей стороны.
    /// </summary>
    /// <returns>True - необходимо проверять, False - иначе.</returns>
    /// <remarks>Поведение по умолчанию - проверять.
    /// Может быть переопределена в наследниках.</remarks>
    public virtual bool NeedValidateOurSignatorySignatureSetting()
    {
      return true;
    }
    
    #region МКДО
    
    /// <summary>
    /// Проверка возможности отправки ответа контрагенту через сервис обмена.
    /// </summary>
    /// <returns>True, если отправка ответа возможна, иначе - false.</returns>
    [Public, Remote]
    public virtual bool CanSendAnswer()
    {
      var exchangeDocumentInfo = Exchange.PublicFunctions.ExchangeDocumentInfo.Remote.GetIncomingExDocumentInfo(_obj);
      return _obj.Versions.Count == 1 && exchangeDocumentInfo != null;
    }
    
    /// <summary>
    /// Отправить ответ на неформализованный документ.
    /// </summary>
    /// <param name="box">Абонентский ящик обмена.</param>
    /// <param name="party">Контрагент.</param>
    /// <param name="certificate">Сертификат.</param>
    /// <param name="isAgent">Признак вызова из фонового процесса. Иначе - пользователем в RX.</param>
    [Public]
    public virtual void SendAnswer(Sungero.ExchangeCore.IBusinessUnitBox box, Parties.ICounterparty party, ICertificate certificate, bool isAgent)
    {
      Exchange.PublicFunctions.Module.SendAnswerToNonformalizedDocument(_obj, party, box, certificate, isAgent);
    }
    
    /// <summary>
    /// Попытаться зарегистрировать документ с настройками по умолчанию.
    /// </summary>
    /// <param name="number">Номер.</param>
    /// <param name="date">Дата.</param>
    /// <returns>True, если регистрация была выполнена.</returns>
    [Public]
    public virtual bool TryExternalRegister(string number, DateTime? date)
    {
      if (string.IsNullOrWhiteSpace(number) || !date.HasValue ||
          _obj.DocumentKind.NumberingType == Docflow.DocumentKind.NumberingType.NotNumerable)
      {
        return false;
      }
      
      var settingType = _obj.DocumentKind.NumberingType == Docflow.DocumentKind.NumberingType.Registrable ?
        Docflow.RegistrationSetting.SettingType.Registration :
        Docflow.RegistrationSetting.SettingType.Numeration;
      
      var dialogParams = Functions.OfficialDocument.GetRegistrationDialogParams(_obj, settingType);
      
      if (dialogParams.DefaultRegister != null)
      {
        _obj.RegistrationDate = date;
        var maxNumberLength = _obj.Info.Properties.RegistrationNumber.Length;
        _obj.RegistrationNumber = number.Length > maxNumberLength ? number.Substring(0, maxNumberLength) : number;
        _obj.RegistrationState = Docflow.OfficialDocument.RegistrationState.Registered;
        _obj.DocumentRegister = dialogParams.DefaultRegister;
        return true;
      }
      return false;
    }
    
    #endregion
    
    /// <summary>
    /// Получить документ по ИД.
    /// </summary>
    /// <param name="id">ИД документа.</param>
    /// <returns>Документ.</returns>
    [Remote(IsPure = true), Public]
    public static Docflow.IOfficialDocument GetOfficialDocument(int id)
    {
      return Docflow.OfficialDocuments.Get(id);
    }
    
    /// <summary>
    /// Создать ответный документ.
    /// </summary>
    /// <returns>Ответный документ.</returns>
    [Remote, Public]
    public virtual Docflow.IOfficialDocument CreateReplyDocument()
    {
      return null;
    }
    
    /// <summary>
    /// Выдать сотруднику права на документ.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    [Public]
    public virtual void GrantAccessRightsToActionItemAttachment(IEmployee employee)
    {
      if (_obj != null && !_obj.AccessRights.CanRead(employee))
        _obj.AccessRights.Grant(employee, DefaultAccessRightsTypes.Read);
    }
    
    /// <summary>
    /// Создать PublicBody документа из html в формате pdf.
    /// </summary>
    /// <param name="sourceHtml">Исходный html.</param>
    [Public]
    public virtual void CreatePdfPublicBodyFromHtml(string sourceHtml)
    {
      if (sourceHtml.Contains("<script"))
      {
        var errorMessage = Sungero.Docflow.OfficialDocuments.Resources.CanNotUseScriptsInHtmlFormat(Sungero.Docflow.Resources.PdfConvertErrorFormat(_obj.Id));
        Logger.Error(errorMessage);
      }
      
      var pdfStream = new System.IO.MemoryStream();
      var sourceBytes = System.Text.Encoding.UTF8.GetBytes(sourceHtml);
      var converter = new AsposeExtensions.Converter();
      try
      {
        using (var inputStream = new System.IO.MemoryStream(sourceBytes))
          pdfStream = converter.ConvertHtmlToPdf(inputStream);
      }
      catch (AsposeExtensions.PdfConvertException e)
      {
        Logger.Error(Sungero.Docflow.Resources.PdfConvertErrorFormat(_obj.Id), e.InnerException);
      }
      catch (Exception e)
      {
        Logger.Error(Sungero.Docflow.Resources.PdfConvertErrorFormat(_obj.Id), e);
      }
      
      _obj.LastVersion.PublicBody.Write(pdfStream);
      // Заполнение расширения обязательно. Делать это нужно после создания PublicBody, иначе затрется оригинальное расширение.
      _obj.LastVersion.AssociatedApplication = AssociatedApplications.GetByExtension("pdf");
      pdfStream.Close();
    }
    
    /// <summary>
    /// Удалить документ.
    /// </summary>
    /// <param name="documentId">ID документа.</param>
    [Public, Remote]
    public static void DeleteDocument(int documentId)
    {
      var doc = OfficialDocuments.GetAll(x => x.Id == documentId).FirstOrDefault();
      
      if (doc != null)
        OfficialDocuments.Delete(doc);
    }
    
    #region Диалог создания поручений по телу документа
    
    /// <summary>
    /// Удаление поручения, созданного по документу.
    /// </summary>
    /// <param name="actionItemId">ИД задачи, которую необходимо удалить.</param>
    /// <returns>True, если удаление прошло успешно.</returns>
    [Remote]
    public static bool TryDeleteActionItemTask(int actionItemId)
    {
      try
      {
        var task = RecordManagement.ActionItemExecutionTasks.Get(actionItemId);
        if (task.AccessRights.CanDelete())
          RecordManagement.ActionItemExecutionTasks.Delete(task);
        else
          return false;
      }
      catch
      {
        return false;
      }

      return true;
    }

    /// <summary>
    /// Получение созданных поручений по документу.
    /// </summary>
    /// <returns>Созданные поручения по документу.</returns>
    [Remote]
    public virtual IQueryable<RecordManagement.IActionItemExecutionTask> GetCreatedActionItems()
    {
      var typeGuid = _obj.GetEntityMetadata().GetOriginal().NameGuid;
      var groupId = Docflow.PublicConstants.Module.TaskMainGroup.ActionItemExecutionTask;
      return RecordManagement.ActionItemExecutionTasks.GetAll(a => a.AttachmentDetails
                                                              .Any(d => d.EntityTypeGuid == typeGuid &&
                                                                   d.GroupId == groupId &&
                                                                   d.AttachmentId == _obj.Id));
    }
    
    /// <summary>
    /// Создать поручения по документу.
    /// </summary>
    /// <returns>Список созданных поручений.</returns>
    [Remote, Public]
    public virtual List<RecordManagement.IActionItemExecutionTask> CreateActionItemsFromDocument()
    {
      var resultList = new List<RecordManagement.IActionItemExecutionTask>();
      
      var lastVersion = _obj.Versions.OrderByDescending(v => v.Number).FirstOrDefault();
      if (lastVersion == null)
        return resultList;
      var culture = TenantInfo.Culture;
      
      // TODO 63572 Криво парсятся даты вида 27.02 на 2008 сервере.
      if (culture.TwoLetterISOLanguageName == "ru" && culture.DateTimeFormat.MonthDayPattern == "MMMM dd")
      {
        if (culture.IsReadOnly)
          culture = (System.Globalization.CultureInfo)culture.Clone();
        culture.DateTimeFormat.MonthDayPattern = "dd MMMM";
      }
      
      using (Sungero.Core.CultureInfoExtensions.SwitchTo(culture))
      {
        using (var stream = new System.IO.MemoryStream())
        {
          lastVersion.Body.Read().CopyTo(stream);
          Dictionary<string, string[]> tagTexts = null;
          try
          {
            var tagReader = new AsposeExtensions.TagReader(stream);
            tagTexts = tagReader.GetColumnByTag(OfficialDocuments.Resources.ActionItemCreationDialogActionItemTag,
                                                OfficialDocuments.Resources.ActionItemCreationDialogResponsibleTag,
                                                OfficialDocuments.Resources.ActionItemCreationDialogDeadlineTag);
          }
          catch (Exception ex)
          {
            Logger.Error("Error while reading tags from the document", ex);
            throw new AppliedCodeException(OfficialDocuments.Resources.ActionItemCreationDialogException);
          }

          if (tagTexts.Count == 0)
            return resultList;

          for (int i = 0; i < tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogActionItemTag].Length; i++)
          {
            if (tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogActionItemTag].Length == 0
                || tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogActionItemTag][i] == string.Empty
                || tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogDeadlineTag].Length == 0
                || tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogDeadlineTag][i] == string.Empty
                || tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogResponsibleTag].Length == 0
                || tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogResponsibleTag][i] == string.Empty)
              continue;
            else
            {
              var actionItem = RecordManagement.PublicFunctions.Module.Remote.CreateActionItemExecution(_obj);
              
              if (tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogActionItemTag].Length > 0 &&
                  tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogActionItemTag][i] != string.Empty)
                actionItem.ActionItem = tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogActionItemTag][i];

              if (tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogDeadlineTag].Length > 0 &&
                  tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogDeadlineTag][i] != string.Empty)
              {
                var dateTimeText = tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogDeadlineTag][i].ToLower().Trim('.');
                
                // TODO Shklyaev: Убрать, когда исправят Bug 65098.
                var dateTimeTextRegex = System.Text.RegularExpressions.Regex.Match(dateTimeText, @"((\s|\d)г)$");
                if (dateTimeTextRegex.Success)
                  dateTimeText = dateTimeText.Substring(0, dateTimeText.Length - 1);
                
                if (!dateTimeText.Contains("феврал") && !dateTimeText.Contains("сентябр") && !dateTimeText.Contains("ноябр"))
                {
                  dateTimeText = dateTimeText
                    .Replace("февр", "фев")
                    .Replace("сент", "сен")
                    .Replace("нояб", "ноя");
                }
                
                DateTime deadline;
                if (Calendar.TryParseDateTime(dateTimeText, out deadline) &&
                    (deadline.HasTime() && Calendar.UserNow <= deadline || !deadline.HasTime() && Calendar.UserToday <= deadline))
                  actionItem.Deadline = deadline.FromUserTime();
                else
                  actionItem.Deadline = null;
              }

              // Может подставиться исполнитель из документа, очищаем.
              actionItem.Assignee = null;
              
              if (tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogResponsibleTag].Length > 0 &&
                  tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogResponsibleTag][i] != string.Empty)
              {
                var assignees = tagTexts[OfficialDocuments.Resources.ActionItemCreationDialogResponsibleTag][i]
                  .Trim().Split(',', ';', '\r')
                  .Where(n => !string.IsNullOrEmpty(n))
                  .Select(n => n.Trim())
                  .ToList();
                if (assignees.Count > 1)
                {
                  actionItem.Assignee = Company.PublicFunctions.Employee.Remote.GetEmployeeByName(assignees.First());
                  foreach (var assignee in assignees.Skip(1))
                  {
                    var employee = Company.PublicFunctions.Employee.Remote.GetEmployeeByName(assignee);
                    if (employee != null && !Equals(actionItem.Assignee, employee) &&
                        !actionItem.CoAssignees.Any(x => Equals(x.Assignee, employee)))
                    {
                      var newAssignee = actionItem.CoAssignees.AddNew();
                      newAssignee.Assignee = employee;
                    }
                  }
                }
                else if (assignees.Count == 1)
                  actionItem.Assignee = Company.PublicFunctions.Employee.Remote.GetEmployeeByName(assignees.Single());
              }

              Functions.OfficialDocument.FillActionItemExecutionTaskOnCreatedFromDocument(_obj, actionItem);

              var currentEmployee = Company.Employees.Current;
              if (!Equals(currentEmployee, actionItem.Assignee) && !actionItem.CoAssignees.Any(a => Equals(currentEmployee, a.Assignee)))
              {
                actionItem.IsUnderControl = true;
                actionItem.Supervisor = currentEmployee;
              }
              foreach (var property in actionItem.State.Properties)
                property.IsRequired = false;
              
              foreach (var actionItemPart in actionItem.ActionItemParts)
                foreach (var property in actionItemPart.State.Properties)
                  property.IsRequired = false;
              
              // TODO баг 64473 - нет прав у работника, создавшего поручения, если поручение выдается от имени руководителя.
              actionItem.AccessRights.Grant(currentEmployee, DefaultAccessRightsTypes.Change);
              actionItem.Save();
              resultList.Add(actionItem);
            }
          }

        }
      }
      return resultList;
    }
    
    /// <summary>
    /// Заполнение свойств поручения, созданного по документу.
    /// </summary>
    /// <param name="actionItem">Поручение, созданное по документу.</param>
    public virtual void FillActionItemExecutionTaskOnCreatedFromDocument(RecordManagement.IActionItemExecutionTask actionItem)
    {
      
    }
    
    /// <summary>
    /// Получить обновленный список поручений.
    /// </summary>
    /// <param name="ids">Список Id поручений.</param>
    /// <returns>Обновленный список поручений.</returns>
    [Remote]
    public static List<RecordManagement.IActionItemExecutionTask> GetActionItemsExecutionTasks(List<int> ids)
    {
      return RecordManagement.ActionItemExecutionTasks.GetAll(t => ids.Contains(t.Id)).ToList();
    }
    
    #endregion
    
    #region Генерация PDF с отметкой об ЭП
    
    /// <summary>
    /// Преобразовать документ в PDF с наложением отметки об ЭП.
    /// </summary>
    /// <returns>Результат преобразования.</returns>
    [Remote]
    public virtual Structures.OfficialDocument.СonversionToPdfResult ConvertToPdfWithSignatureMark()
    {
      var versionId = _obj.LastVersion.Id;
      var info = this.ValidateDocumentBeforeConvertion(versionId);
      if (info.HasErrors)
        return info;
      
      // Документ МКДО.
      if (Exchange.ExchangeDocumentInfos.GetAll().Any(x => Equals(x.Document, _obj) && x.VersionId == versionId) ||
         (AccountingDocumentBases.Is(_obj) && AccountingDocumentBases.As(_obj).IsFormalized == true))
      {
        Exchange.PublicFunctions.Module.Remote.GeneratePublicBody(_obj.Id);
        info.IsOnConvertion = true;
        info.HasErrors = false;
      }
      else if (this.CanConvertToPdfInteractively())
      {
        // Способ преобразования: интерактивно.
        info = this.ConvertToPdfAndAddSignatureMark(versionId);
        info.IsFastConvertion = true;
        info.ErrorTitle = OfficialDocuments.Resources.ConvertionErrorTitleBase;
      }
      else
      {
        // Способ преобразования: асинхронно.
        var asyncConvertToPdf = Docflow.AsyncHandlers.ConvertDocumentToPdf.Create();
        asyncConvertToPdf.DocumentId = _obj.Id;
        asyncConvertToPdf.VersionId = versionId;
        asyncConvertToPdf.UserId = Users.Current.Id;
        asyncConvertToPdf.ExecuteAsync();
        
        info.IsOnConvertion = true;
        info.HasErrors = false;
      }
      
      return info;
    }
    
    /// <summary>
    /// Преобразовать документ в PDF и поставить отметку об ЭП.
    /// </summary>
    /// <param name="versionId">Id версии документа.</param>
    /// <returns>Результат преобразования в PDF.</returns>
    [Remote]
    public virtual Structures.OfficialDocument.СonversionToPdfResult ConvertToPdfAndAddSignatureMark(int versionId)
    {
      var signatureMark = this.GetSignatureMarkAsHtml(versionId);
      return this.GeneratePublicBodyWithSignatureMark(versionId, signatureMark);
    }
    
    /// <summary>
    /// Проверить документ до преобразования в PDF.
    /// </summary>
    /// <param name="versionId">Id версии документа.</param>
    /// <returns>Результат проверки перед преобразованием документа.</returns>
    public virtual Structures.OfficialDocument.СonversionToPdfResult ValidateDocumentBeforeConvertion(int versionId)
    {
      var info = Structures.OfficialDocument.СonversionToPdfResult.Create();
      info.HasErrors = true;
      
      // Документ МКДО.
      if (Exchange.ExchangeDocumentInfos.GetAll().Any(x => Equals(x.Document, _obj) && x.VersionId == versionId))
      {
        info.HasErrors = false;
        return info;
      }
      
      // Формализованный документ.
      if (AccountingDocumentBases.Is(_obj) && AccountingDocumentBases.As(_obj).IsFormalized == true)
      {
        info.HasErrors = false;
        return info;
      }
      
      // Проверить наличие версии.
      var version = _obj.Versions.FirstOrDefault(x => x.Id == versionId);
      if (version == null)
      {
        info.ErrorTitle = OfficialDocuments.Resources.ConvertionErrorTitleBase;
        info.ErrorMessage = OfficialDocuments.Resources.NoVersionError;
        return info;
      }
      
      // Требуемая версия утверждена.
      if (!Signatures.Get(version)
          .Any(s => s.IsExternal != true && s.SignatureType == SignatureType.Approval))
      {
        info.ErrorTitle = OfficialDocuments.Resources.LastVersionNotApprovedTitle;
        info.ErrorMessage = OfficialDocuments.Resources.LastVersionNotApproved;
        return info;
      }
      
      // Формат не поддерживается.
      var versionExtension = version.BodyAssociatedApplication.Extension.ToLower();
      var versionExtensionIsSupported = AsposeExtensions.Converter.CheckIfExtensionIsSupported(versionExtension);
      if (!versionExtensionIsSupported)
      {
        info.ErrorTitle = OfficialDocuments.Resources.ConvertionErrorTitleBase;
        info.ErrorMessage = OfficialDocuments.Resources.ExtensionNotSupportedFormat(versionExtension);
        return info;
      }
      
      // Валидация подписи.
      var signature = Functions.OfficialDocument.GetSignatureForMark(_obj, versionId);
      var separator = ". ";
      var validationError = Docflow.Functions.Module.GetSignatureValidationErrorsAsString(signature, separator);
      if (!string.IsNullOrEmpty(validationError))
      {
        info.ErrorTitle = OfficialDocuments.Resources.SignatureNotValidErrorTitle;
        info.ErrorMessage = string.Format(OfficialDocuments.Resources.SignatureNotValidError, validationError);
        return info;
      }
      
      info.HasErrors = false;
      return info;
    }
    
    /// <summary>
    /// Сгенерировать PublicBody документа с отметкой об ЭП.
    /// </summary>
    /// <param name="versionId">Id версии для генерации.</param>
    /// <param name="signatureMark">Отметка об ЭП (html).</param>
    /// <returns>Информация о результате генерации PublicBody для версии документа.</returns>
    public virtual Structures.OfficialDocument.СonversionToPdfResult GeneratePublicBodyWithSignatureMark(int versionId, string signatureMark)
    {
      return Functions.Module.GeneratePublicBodyWithSignatureMark(_obj, versionId, signatureMark);
    }
    
    /// <summary>
    /// Получить отметку об ЭП.
    /// </summary>
    /// <param name="versionId">Id версии для генерации.</param>
    /// <returns>Изображение отметки об ЭП в виде html.</returns>
    [Public]
    public virtual string GetSignatureMarkAsHtml(int versionId)
    {
      return Functions.Module.GetSignatureMarkAsHtml(_obj, versionId);
    }
    
    /// <summary>
    /// Обёртка для функции из ApprovalReviewAssignment.
    /// </summary>
    /// <param name="subject">Атрибуты содержания сертификата одной строкой.</param>
    /// <returns>Распарсенные атрибуты содержания сертификата.</returns>
    /// <remarks>
    /// Нужна для того, чтобы можно было переиспользовать метод парсинга при перекрытии GetHtmlMark.
    /// Альтернатива этой обёртке - присвоить изначальному методу и отдаваемой им структуре атрибут Public.
    /// </remarks>
    public static Sungero.Docflow.Structures.Module.ICertificateSubject ParseSignatureSubject(string subject)
    {
      return Functions.ApprovalReviewAssignment.ParseSignatureSubject(subject);
    }
    
    /// <summary>
    /// Получить электронную подпись для простановки отметки.
    /// </summary>
    /// <param name="versionId">Номер версии.</param>
    /// <returns>Электронная подпись.</returns>
    [Public]
    public virtual Sungero.Domain.Shared.ISignature GetSignatureForMark(int versionId)
    {
      var version = _obj.Versions.FirstOrDefault(x => x.Id == versionId);
      if (version == null)
        return null;
      
      // Только утверждающие подписи.
      var versionSignatures = Signatures.Get(version)
        .Where(s => s.IsExternal != true && s.SignatureType == SignatureType.Approval)
        .ToList();
      if (!versionSignatures.Any())
        return null;
      
      // В приоритете подпись сотрудника из поля "Подписал". Квалифицированная ЭП приоритетнее простой.
      return versionSignatures
        .OrderByDescending(s => Equals(s.Signatory, _obj.OurSignatory))
        .ThenBy(s => s.SignCertificate == null)
        .ThenByDescending(s => s.SigningDate)
        .FirstOrDefault();
    }
    
    /// <summary>
    /// Определить возможность интерактивной конвертации документа.
    /// </summary>
    /// <returns>True - возможно, False - иначе.</returns>
    public virtual bool CanConvertToPdfInteractively()
    {
      return Functions.Module.CanConvertToPdfInteractively(_obj);
    }
    
    #endregion
    
    #region Интеллектуальная обработка
    
    /// <summary>
    /// Сохранить результат верификации заполнения свойств.
    /// </summary>
    [Public]
    public virtual void StoreVerifiedPropertiesValues()
    {
      // Сохранять только в том случае, когда статус верификации меняется на "Завершено".
      var documentParams = ((Domain.Shared.IExtendedEntity)_obj).Params;
      if (!documentParams.ContainsKey(Docflow.PublicConstants.OfficialDocument.NeedStoreVerifiedPropertiesValuesParamName))
        return;
      
      var recognitionInfo = Commons.PublicFunctions.EntityRecognitionInfo.Remote.GetEntityRecognitionInfo(_obj);
      if (recognitionInfo == null)
        return;
      
      // Взять только заполненные свойства самого документа. Свойства-коллекции записываются через точку.
      var linkedFacts = recognitionInfo.Facts
        .Where(x => !string.IsNullOrEmpty(x.PropertyName) && !x.PropertyName.Any(с => с == '.'));
      
      // Взять только измененные пользователем свойства.
      var type = _obj.GetType();
      foreach (var linkedFact in linkedFacts)
      {
        var propertyName = linkedFact.PropertyName;
        var property = type.GetProperties().Where(p => p.Name == propertyName).LastOrDefault();
        if (property != null)
        {
          object propertyValue = property.GetValue(_obj);
          var propertyStringValue = Commons.PublicFunctions.Module.GetValueAsString(propertyValue);
          if (!string.IsNullOrWhiteSpace(propertyStringValue) && !Equals(propertyStringValue, linkedFact.PropertyValue))
            linkedFact.VerifiedValue = propertyStringValue;
        }
      }
      documentParams.Remove(Docflow.PublicConstants.OfficialDocument.NeedStoreVerifiedPropertiesValuesParamName);
    }
    
    #endregion
    
    /// <summary>
    /// Определить, есть ли активные задачи согласования по регламенту документа.
    /// </summary>
    /// <returns>True, если есть.</returns>
    [Remote]
    public bool HasApprovalTasksWithCurrentDocument()
    {
      var anyTasks = false;

      Sungero.Core.AccessRights.AllowRead(
        () =>
        {
          var docGuid = _obj.GetEntityMetadata().GetOriginal().NameGuid;
          var approvalTaskDocumentGroupGuid = Constants.Module.TaskMainGroup.ApprovalTask;
          anyTasks = ApprovalTasks.GetAll()
            .Where(t => t.Status == Workflow.Task.Status.InProcess ||
                   t.Status == Workflow.Task.Status.Suspended)
            .Where(t => t.AttachmentDetails
                   .Any(att => att.AttachmentId == _obj.Id && att.EntityTypeGuid == docGuid &&
                        att.GroupId == approvalTaskDocumentGroupGuid))
            .Any();
          
        });
      
      return anyTasks;
    }
    
    /// <summary>
    /// Фильтрация дел для документа.
    /// </summary>
    /// <param name="query">Исходные дела для документа.</param>
    /// <returns>Отфильтрованные дела для документа.</returns>
    [Public]
    public virtual IQueryable<ICaseFile> CaseFileFiltering(IQueryable<ICaseFile> query)
    {
      if (_obj.BusinessUnit != null)
        query = query.Where(x => Equals(x.BusinessUnit, _obj.BusinessUnit) || x.BusinessUnit == null);
      
      return query;
    }
    
    /// <summary>
    /// Изменить статус документа на "В разработке".
    /// </summary>
    public virtual void SetLifeCycleStateDraft()
    {
      if (_obj.LifeCycleState == null || _obj.LifeCycleState == Docflow.OfficialDocument.LifeCycleState.Obsolete)
      {
        Logger.DebugFormat("UpdateLifeCycleState: Document {0} changed LifeCycleState to 'Draft'.", _obj.Id);
        _obj.LifeCycleState = Docflow.OfficialDocument.LifeCycleState.Draft;
      }
    }
  }
}