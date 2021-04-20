﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Company;
using Sungero.Content;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Workflow;

namespace Sungero.Docflow.Shared
{
  public class ModuleFunctions
  {
    #region Упаковка/распаковка результатов ремот-функций
    
    // TODO: посмотреть в сторону структур.
    
    /// <summary>
    /// Упаковка словаря в строку для передачи через Remote функции.
    /// </summary>
    /// <param name="result">Словарь.</param>
    /// <returns>Строка с упакованным словарем.</returns>
    [Public]
    public static string BoxToString(System.Collections.Generic.Dictionary<string, bool> result)
    {
      var valueDelimiter = "-";
      var rowDelimiter = "|";
      return string.Join(rowDelimiter, result.Select(d => string.Format("{0}{2}{1}", d.Key, d.Value, valueDelimiter)));
    }
    
    /// <summary>
    /// Распаковка словаря из строки.
    /// </summary>
    /// <param name="result">Строка с упакованным словарем.</param>
    /// <returns>Словарь.</returns>
    [Public]
    public static System.Collections.Generic.Dictionary<string, bool> UnboxDictionary(string result)
    {
      var valueDelimiter = '-';
      var rowDelimiter = '|';
      
      var dictionary = new Dictionary<string, bool>();
      foreach (var row in result.Split(rowDelimiter))
      {
        var key = row.Split(valueDelimiter)[0];
        var value = bool.Parse(row.Split(valueDelimiter)[1]);
        dictionary.Add(key, value);
      }
      
      return dictionary;
    }
    
    #endregion
    
    #region Копирование номенклатуры дел
    
    /// <summary>
    /// Получить сообщение о результатах копирования номенклатуры дел.
    /// </summary>
    /// <param name="targetPeriodStartDate">Начало целевого периода.</param>
    /// <param name="targetPeriodEndDate">Конец целевого периода.</param>
    /// <param name="success">Количество успешно скопированных дел.</param>
    /// <param name="failed">Количество дел с ошибками при копировании.</param>
    /// <returns>Сообщение о результатах копирования номенклатуры дел.</returns>
    public virtual string GetCopyingCaseFilesTotalsMessage(DateTime targetPeriodStartDate,
                                                           DateTime targetPeriodEndDate,
                                                           int success,
                                                           int failed)
    {
      var year = this.GetCopyingCaseFilesTargetPeriodAsString(targetPeriodStartDate, targetPeriodEndDate);
      return this.AppendCaseFilesHyperlinkTo(Resources.CaseFileCopyingResultFormat(year, success));
    }
    
    /// <summary>
    /// Получить сообщение о том, что номенклатура дел была скопирована ранее.
    /// </summary>
    /// <param name="targetPeriodStartDate">Начало целевого периода.</param>
    /// <param name="targetPeriodEndDate">Конец целевого периода.</param>
    /// <returns>Сообщение о том, что номенклатура дел была скопирована ранее.</returns>
    public virtual string GetAlreadyCopiedCaseFilesMessage(DateTime targetPeriodStartDate,
                                                       DateTime targetPeriodEndDate)
    {
      var year = this.GetCopyingCaseFilesTargetPeriodAsString(targetPeriodStartDate, targetPeriodEndDate);
      return this.AppendCaseFilesHyperlinkTo(Resources.CaseFileCopyingAlreadyDoneFormat(year));
    }
    
    /// <summary>
    /// Получить сообщение о том, что нет дел, соответствующих параметрам, для копирования.
    /// </summary>
    /// <param name="targetPeriodStartDate">Начало целевого периода.</param>
    /// <param name="targetPeriodEndDate">Конец целевого периода.</param>
    /// <returns>Сообщение о том, что нет дел, соответствующих параметрам, для копирования.</returns>
    public virtual string GetNoCaseFilesToCopyMessage(DateTime targetPeriodStartDate,
                                                       DateTime targetPeriodEndDate)
    {
      var year = this.GetCopyingCaseFilesTargetPeriodAsString(targetPeriodStartDate, targetPeriodEndDate);
      return this.AppendCaseFilesHyperlinkTo(Resources.HasNoCaseFilesToCopyFormat(year));
    }
    
    /// <summary>
    /// Получить представление целевого периода копирования номенклатуры в виде строки.
    /// </summary>
    /// <param name="targetPeriodStartDate">Начало целевого периода.</param>
    /// <param name="targetPeriodEndDate">Конец целевого периода.</param>
    /// <returns>Представление целевого периода копирования номенклатуры в виде строки.</returns>
    public virtual string GetCopyingCaseFilesTargetPeriodAsString(DateTime targetPeriodStartDate,
                                                                  DateTime targetPeriodEndDate)
    {
      // Dmitriev_IA: Даты начала и конца целевого периода копирования формируются программно
      //              в клиентской функции GetCaseFilesCopyDialogTargetPeriod() модуля Docflow
      //              и всегда принадлежат одному году.
      return targetPeriodStartDate.Year.ToString();
    }
    
    /// <summary>
    /// Добавить гиперссылку на номенклатуру дел к строке.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <returns>Строка, дополненная ссылкой на номенклатуру дел.</returns>
    public virtual string AppendCaseFilesHyperlinkTo(string source)
    {
      return string.Format("{0}{1}{2}", source, Environment.NewLine, Hyperlinks.Get(CaseFiles.Info));
    }
    
    #endregion
    
    /// <summary>
    /// Проверить наличие подчиненных поручений.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>True, если есть подпоручения, иначе false.</returns>
    public static bool HasSubActionItems(ITask task)
    {
      return RecordManagement.PublicFunctions.ActionItemExecutionTask.Remote.HasSubActionItems(task);
    }
    
    /// <summary>
    /// Проверить наличие подчиненных поручений.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="status">Статус поручения.</param>
    /// <returns>True, если есть подпоручения, иначе false.</returns>
    public static bool HasSubActionItems(ITask task, Enumeration status)
    {
      return RecordManagement.PublicFunctions.ActionItemExecutionTask.Remote.HasSubActionItems(task, status);
    }
    
    /// <summary>
    /// Проверить наличие подчиненных поручений.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="status">Статус поручения.</param>
    /// <param name="addressee">Адресат.</param>
    /// <returns>True, если есть подпоручения, иначе false.</returns>
    public static bool HasSubActionItems(ITask task, Enumeration status, IEmployee addressee)
    {
      return RecordManagement.PublicFunctions.ActionItemExecutionTask.Remote.HasSubActionItems(task, status, addressee);
    }
    
    /// <summary>
    /// Получить список поручений для формирования блока резолюции задачи на согласование.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="status">Статус поручений (исключаемый).</param>
    /// <param name="addressee">Адресат.</param>
    /// <returns>Список поручений.</returns>
    public static List<ITask> GetActionItemsForResolution(ITask task, Enumeration status, IEmployee addressee)
    {
      return RecordManagement.PublicFunctions.ActionItemExecutionTask.Remote.GetActionItemsForResolution(task, status, addressee);
    }
    
    /// <summary>
    /// Получить информацию по поручению для вывода резолюции.
    /// </summary>
    /// <param name="task">Поручение.</param>
    /// <returns>Список значений строк:
    /// текст поручения;
    /// исполнитель (-и);
    /// срок;
    /// контролер.</returns>
    public static List<string> ActionItemInfoProvider(ITask task)
    {
      return RecordManagement.PublicFunctions.ActionItemExecutionTask.Remote.ActionItemInfoProvider(task);
    }
    
    /// <summary>
    /// Показать сообщение Dialog.NotifyMessage через Reflection.
    /// </summary>
    /// <param name="message">Сообщение.</param>
    [Public]
    public static void TryToShowNotifyMessage(string message)
    {
      var dialogs = Type.GetType("Sungero.Core.Dialogs, Sungero.Domain.ClientBase");
      if (dialogs != null)
        dialogs.InvokeMember("NotifyMessage", System.Reflection.BindingFlags.InvokeMethod, null, null, new string[1] { message });
    }
    
    /// <summary>
    /// Получить последнего утвердившего документ.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Подписавший.</returns>
    public static IEmployee GetDocumentLastApprover(IElectronicDocument document)
    {
      return Employees.As(Signatures.Get(document.LastVersion)
                          .Where(s => s.SignatureType == SignatureType.Approval)
                          .OrderByDescending(s => s.SigningDate)
                          .Select(s => s.Signatory)
                          .FirstOrDefault());
    }

    /// <summary>
    /// Подобрать для документа подходящее хранилище.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <returns>Наиболее подходящее хранилище для документа.</returns>
    public virtual IStorage GetStorageByPolicies(IOfficialDocument document)
    {
      var policies = StoragePolicies.GetAllCached().Where(r => r.Status == Docflow.StoragePolicyBase.Status.Active &&
                                                          (document.DocumentKind == null ||
                                                           !r.DocumentKinds.Any() ||
                                                           r.DocumentKinds.Any(k => Equals(k.DocumentKind, document.DocumentKind))));

      var policy = policies.OrderByDescending(p => p.Priority).FirstOrDefault();
      if (policy != null)
        return policy.Storage;
      
      return null;
    }
    
    /// <summary>
    /// Привести дату к тенантному времени.
    /// </summary>
    /// <param name="datetime">Дата.</param>
    /// <returns>Дата во времени тенанта.</returns>
    [Public]
    public static DateTime ToTenantTime(DateTime datetime)
    {
      return datetime.Kind == DateTimeKind.Utc ? datetime.FromUtcTime() : datetime;
    }
    
    /// <summary>
    /// Заменить первый символ строки на прописной.
    /// </summary>
    /// <param name="label">Исходная строка.</param>
    /// <returns>Результирующая строка.</returns>
    [Public]
    public static string ReplaceFirstSymbolToUpperCase(string label)
    {
      if (string.IsNullOrWhiteSpace(label))
        return string.Empty;
      
      return string.Format("{0}{1}",
                           char.ToUpper(label[0]),
                           label.Length > 1 ? label.Substring(1) : string.Empty);
    }
    
    /// <summary>
    /// Проверить, состоит ли строка из ASCII символов.
    /// </summary>
    /// <param name="value"> Строка.</param>
    /// <returns> Результат.</returns>
    [Public]
    public static bool IsASCII(string value)
    {
      // Если длина строки в байтах = длине строки в символах, то строка состоит из аски символов.
      if (string.IsNullOrEmpty(value))
        return true;
      return System.Text.Encoding.UTF8.GetByteCount(value) == value.Length;
    }
    
    /// <summary>
    /// Заменить первый символ строки на строчный.
    /// </summary>
    /// <param name="label">Исходная строка.</param>
    /// <returns>Результирующая строка.</returns>
    [Public]
    public static string ReplaceFirstSymbolToLowerCase(string label)
    {
      if (string.IsNullOrWhiteSpace(label))
        return string.Empty;
      
      return string.Format("{0}{1}",
                           char.ToLower(label[0]),
                           label.Length > 1 ? label.Substring(1) : string.Empty);
    }
    
    /// <summary>
    /// Получить склоненное имя для числа.
    /// </summary>
    /// <param name="number">Число.</param>
    /// <param name="singleName">Имя в единственном числе.</param>
    /// <param name="genitiveName">Имя в родительном падеже.</param>
    /// <param name="pluralName">Имя во множественном числе.</param>
    /// <returns>Склоненное имя числа.</returns>
    [Public]
    public static string GetNumberDeclination(int number,
                                              CommonLibrary.LocalizedString singleName,
                                              CommonLibrary.LocalizedString genitiveName,
                                              CommonLibrary.LocalizedString pluralName)
    {
      // TODO: 35010.
      if (singleName.Culture.TwoLetterISOLanguageName == "ru")
      {
        number = number % 100;

        // Числа, заканчивающиеся на число с 11 до 19, всегда именовать в родительном падеже.
        if (number >= 11 && number <= 19)
          return pluralName;

        var i = number % 10;
        switch (i)
        {
          case 1:
            return singleName;
          case 2:
          case 3:
          case 4:
            return genitiveName;
          default:
            return pluralName;
        }
      }
      
      return number == 1 ? singleName : pluralName;
    }
    
    #region Работа с сотрудниками
    
    /// <summary>
    /// Получить секретаря руководителя.
    /// </summary>
    /// <param name="manager">Руководитель.</param>
    /// <returns>Секретарь.</returns>
    [Public]
    public static IEmployee GetSecretary(IEmployee manager)
    {
      return ManagersAssistants
        .GetAllCached(m => manager.Equals(m.Manager))
        .Where(m => m.Status == CoreEntities.DatabookEntry.Status.Active && m.Assistant.Status == CoreEntities.DatabookEntry.Status.Active)
        .Select(m => m.Assistant)
        .FirstOrDefault();
    }
    
    /// <summary>
    /// Получить секретаря руководителя.
    /// </summary>
    /// <param name="manager">Руководитель.</param>
    /// <returns>Список секретарей.</returns>
    [Public]
    public static List<IManagersAssistant> GetSecretaries(IEmployee manager)
    {
      return ManagersAssistants
        .GetAllCached(m => manager.Equals(m.Manager))
        .Where(m => m.Status == CoreEntities.DatabookEntry.Status.Active && m.Assistant.Status == CoreEntities.DatabookEntry.Status.Active)
        .ToList();
    }
    
    /// <summary>
    /// Получить начальника секретаря.
    /// </summary>
    /// <param name="secretary">Секретарь.</param>
    /// <returns>Руководитель.</returns>
    [Public]
    public static IEmployee GetSecretaryManager(IEmployee secretary)
    {
      return ManagersAssistants
        .GetAllCached(m => secretary.Equals(m.Assistant))
        .Where(m => m.Status == CoreEntities.DatabookEntry.Status.Active && m.Manager.Status == CoreEntities.DatabookEntry.Status.Active)
        .Select(m => m.Manager)
        .FirstOrDefault();
    }
    
    #endregion
    
    /// <summary>
    /// Сформировать название таблицы для отчета.
    /// </summary>
    /// <param name="reportName">Название отчёта.</param>
    /// <param name="userId">Id пользователя, запустившего отчёт.</param>
    /// <returns>Название таблицы вида "Sungero_Reports_{reportName}_{userId}_{randomNumber}".</returns>
    [Public]
    public static string GetReportTableName(string reportName, int userId)
    {
      var randomNumber = Math.Abs(Environment.TickCount);
      
      return string.Format("Sungero_Reports_{0}_{1}_{2}", reportName, userId, randomNumber);
    }
    
    /// <summary>
    /// Сформировать название таблицы для отчета.
    /// </summary>
    /// <param name="report">Отчёт.</param>
    /// <param name="userId">Id пользователя, запустившего отчёт.</param>
    /// <returns>Название таблицы вида "Sungero_Reports_{reportName}_{userId}_{randomNumber}".</returns>
    [Public]
    public static string GetReportTableName(Reporting.IReport report, int userId)
    {
      var reportName = report.Info.Name;
      return GetReportTableName(reportName, userId);
    }
    
    /// <summary>
    /// Сформировать название таблицы для отчета.
    /// </summary>
    /// <param name="report">Отчёт.</param>
    /// <param name="userId">Id пользователя, запустившего отчёт.</param>
    /// <param name="postfix">Постфикс таблицы.</param>
    /// <returns>Название таблицы вида "Sungero_Reports_{reportName}_{userId}_{postfix}".</returns>
    [Public]
    public static string GetReportTableName(Reporting.IReport report, int userId, string postfix)
    {
      var prefix = GetReportTableName(report, userId);
      
      if (string.IsNullOrWhiteSpace(postfix))
        return prefix;
      
      return string.Format("{0}_{1}", prefix, postfix);
    }
    
    /// <summary>
    /// Сформировать название таблицы для отчета.
    /// </summary>
    /// <param name="reportName">Название отчета.</param>
    /// <param name="userId">Id пользователя, запустившего отчёт.</param>
    /// <param name="postfix">Постфикс таблицы.</param>
    /// <returns>Название таблицы вида "Sungero_Reports_{reportName}_{userId}_{postfix}.</returns>
    [Public]
    public static string GetReportTableName(string reportName, int userId, string postfix)
    {
      var prefix = GetReportTableName(reportName, userId);
      
      if (string.IsNullOrWhiteSpace(postfix))
        return prefix;
      
      return string.Format("{0}_{1}", prefix, postfix);
    }
    
    /// <summary>
    /// Получить НОР сотрудника.
    /// Берется из настроек, либо определяется по оргструктуре.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    /// <returns>Наша организация.</returns>
    [Public]
    public static Sungero.Company.IBusinessUnit GetDefaultBusinessUnit(Sungero.Company.IEmployee employee)
    {
      if (employee == null)
        return null;
      
      var setting = Functions.PersonalSetting.GetPersonalSettings(employee);
      return (setting != null && setting.BusinessUnit != null) ?
        setting.BusinessUnit :
        Company.PublicFunctions.BusinessUnit.Remote.GetBusinessUnit(employee);
    }
    
    /// <summary>
    /// Синхронизировать приложения документа и группы вложения.
    /// </summary>
    /// <param name="group">Группа вложения задачи.</param>
    /// <param name="document">Документ.</param>
    [Public]
    public static void SynchronizeAddendaAndAttachmentsGroup(Sungero.Workflow.Interfaces.IWorkflowEntityAttachmentGroup group, IElectronicDocument document)
    {
      if (document == null)
      {
        foreach (var addendum in group.All)
          group.All.Remove(addendum);
        return;
      }

      var documentAddenda = document.Relations.GetRelated(Docflow.Constants.Module.AddendumRelationName);
      foreach (var addendum in group.All.Select(e => ElectronicDocuments.As(e)).Where(d => d != null && !documentAddenda.Contains(d)))
        group.All.Remove(addendum);
      
      var newAddenda = documentAddenda.Where(d => !group.All.Contains(d)).ToList();
      foreach (var addendum in newAddenda)
        group.All.Add(addendum);
    }
    
    #region TrimSpecialSymbols, TrimQuotes
    
    /// <summary>
    /// Убрать лишние кавычки и переносы строк.
    /// </summary>
    /// <param name="subject">Исходная строка.</param>
    /// <returns>Результирующая строка.</returns>
    [Public]
    public static string TrimSpecialSymbols(string subject)
    {
      subject = subject.Replace(Environment.NewLine, " ").Replace("\n", " ");
      subject = subject.Replace("   ", " ").Replace("  ", " ");
      subject = TrimQuotes(subject);
      
      return subject;
    }
    
    /// <summary>
    /// Убрать лишние кавычки и переносы строк.
    /// </summary>
    /// <param name="subject">Исходная строка с форматированием.</param>
    /// <param name="arg0">Аргумент.</param>
    /// <returns>Результирующая строка.</returns>
    /// TODO: Все TrimSpecialSymbols c аргументами подвержены FormatException.
    [Public]
    public static string TrimSpecialSymbols(string subject, object arg0)
    {
      return TrimSpecialSymbols(string.Format(subject, arg0));
    }
    
    /// <summary>
    /// Убрать лишние кавычки и переносы строк.
    /// </summary>
    /// <param name="subject">Исходная строка с форматированием.</param>
    /// <param name="arg0">Аргумент.</param>
    /// <param name="arg1">Второй аргумент.</param>
    /// <returns>Результирующая строка.</returns>
    [Public]
    public static string TrimSpecialSymbols(string subject, object arg0, object arg1)
    {
      return TrimSpecialSymbols(string.Format(subject, arg0, arg1));
    }
    
    /// <summary>
    /// Убрать лишние кавычки и переносы строк.
    /// </summary>
    /// <param name="subject">Исходная строка с форматированием.</param>
    /// <param name="args">Аргументы.</param>
    /// <returns>Результирующая строка.</returns>
    [Public]
    public static string TrimSpecialSymbols(string subject, object[] args)
    {
      return TrimSpecialSymbols(string.Format(subject, args));
    }
    
    /// <summary>
    /// Убрать лишние кавычки.
    /// </summary>
    /// <param name="row">Исходная строка.</param>
    /// <returns>Результирующая строка.</returns>
    [Public]
    public static string TrimQuotes(string row)
    {
      return row.Replace("\"\"\"", "\"").Replace("\"\"", "\"");
    }
    
    /// <summary>
    /// Убрать переносы строк в конце строки.
    /// </summary>
    /// <param name="row">Исходная строка.</param>
    /// <returns>Результирующая строка.</returns>
    [Public]
    public static string TrimEndNewLines(string row)
    {
      if (!string.IsNullOrEmpty(row))
        return row.TrimEnd(Environment.NewLine.ToCharArray());
      else
        return row;
    }
    
    #endregion
    
    /// <summary>
    /// Получить GUID действия.
    /// </summary>
    /// <param name="action">Действие.</param>
    /// <returns>Строка, содержащая GUID.</returns>
    public static string GetActionGuid(Domain.Shared.IActionInfo action)
    {
      var internalAction = action as Domain.Shared.IInternalActionInfo;
      return internalAction == null ? string.Empty : internalAction.NameGuid.ToString();
    }
    
    /// <summary>
    /// Получить действие по отправке документа.
    /// </summary>
    /// <param name="action">Информация о действии.</param>
    /// <returns>Действие по отправке документа.</returns>
    public static IDocumentSendAction GetSendAction(Domain.Shared.IActionInfo action)
    {
      return DocumentSendActions.GetAllCached(a => a.ActionGuid == Functions.Module.GetActionGuid(action)).Single();
    }
    
    /// <summary>
    /// Расcчитать задержку.
    /// </summary>
    /// <param name="deadline">Планируемый срок.</param>
    /// <param name="completed">Реальный срок.</param>
    /// <param name="user">Сотрудник.</param>
    /// <returns>Задержка.</returns>
    [Public]
    public static int CalculateDelay(DateTime? deadline, DateTime completed, IUser user)
    {
      // Для заданий без срока - просрочка невозможна.
      if (!deadline.HasValue)
        return 0;

      // Не просрочено.
      if (completed <= deadline)
        return 0;
      
      var delayInDays = Sungero.CoreEntities.WorkingTime.GetDurationInWorkingDays(deadline.Value.Date, completed.Date, user);
      
      if (delayInDays <= 2)
      {
        // Для заданий без времени взять конец дня.
        var deadlineWithTime = Functions.Module.GetDateWithTime(deadline.Value, user);
        var completedWithTime = Functions.Module.GetDateWithTime(completed, user);
        // Просрочка менее чем на 4 рабочих часа считается за выполненное в срок задание.
        var delayInHours = Sungero.CoreEntities.WorkingTime.GetDurationInWorkingHours(deadlineWithTime, completedWithTime, user);
        if (delayInHours < 4)
          return 0;
      }
      
      // Вычислить просрочку. Просрочка минимум 1 день.
      var delay = delayInDays - 1;
      if (delay == 0)
        delay = 1;
      
      return delay;
    }
    
    /// <summary>
    /// Проверить корректность срока.
    /// </summary>
    /// <param name="user">Пользователь, по чьему календарю проверять срок.</param>
    /// <param name="deadline">Дата, которую сравниваем.</param>
    /// <param name="minDeadline">Минимально допустимая дата.</param>
    /// <returns>True, если сравниваемая дата больше допустимой.</returns>
    /// <remarks>Проверка на строго больше. При равных датах вернёт false.</remarks>
    /// <remarks>Если хоть одна дата не передана(null) - возвращается true.</remarks>
    [Public]
    public static bool CheckDeadline(IUser user, DateTime? deadline, DateTime? minDeadline)
    {
      if (minDeadline == null || deadline == null)
        return true;

      var minDeadlineWithTime = Functions.Module.GetDateWithTime(minDeadline.Value, user);
      var deadlineWithTime = Functions.Module.GetDateWithTime(deadline.Value, user);

      return deadlineWithTime > minDeadlineWithTime;
    }
    
    /// <summary>
    /// Проверить корректность срока.
    /// </summary>
    /// <param name="deadline">Дата, которую сравниваем.</param>
    /// <param name="minDeadline">Минимально допустимая дата.</param>
    /// <returns>True, если сравниваемая дата больше допустимой.</returns>
    /// <remarks>Проверка на строго больше. При равных датах вернёт false.</remarks>
    /// <remarks>Если хоть одна дата не передана(null) - возвращается true.</remarks>
    [Public]
    public static bool CheckDeadline(DateTime? deadline, DateTime? minDeadline)
    {
      if (minDeadline == null || deadline == null)
        return true;

      var minDeadlineWithTime = Functions.Module.GetDateWithTime(minDeadline.Value);
      var deadlineWithTime = Functions.Module.GetDateWithTime(deadline.Value);

      return deadlineWithTime > minDeadlineWithTime;
    }
    
    /// <summary>
    /// Валидация автора задачи.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Сообщение валидации, если автор не является сотрудником, иначе пустая строка.</returns>
    [Public]
    public static string ValidateTaskAuthor(ITask task)
    {
      if (!Sungero.Company.Employees.Is(task.Author))
        return Docflow.Resources.CantSendTaskByNonEmployee;
      
      return string.Empty;
    }
    
    /// <summary>
    /// Валидация автора задачи.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="e">Аргументы действия.</param>
    /// <returns>True, если автор является сотрудником, иначе False.</returns>
    [Public]
    public static bool ValidateTaskAuthor(ITask task, Sungero.Core.IValidationArgs e)
    {
      var authorIsNonEmployeeMessage = ValidateTaskAuthor(task);
      if (!string.IsNullOrWhiteSpace(authorIsNonEmployeeMessage))
      {
        e.AddError(task.Info.Properties.Author, authorIsNonEmployeeMessage);
        return false;
      }
      return true;
    }
    
    /// <summary>
    /// Сформировать имя документа для отчета.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="withHyperlink">Строка с гиперссылкой.</param>
    /// <returns>Имя документа в формате: Имя (ИД: 1, Версия 1/Без версии).</returns>
    [Public]
    public static string FormatDocumentNameForReport(Content.IElectronicDocument document, bool withHyperlink)
    {
      return PublicFunctions.Module.FormatDocumentNameForReport(document, 0, withHyperlink);
    }
    
    /// <summary>
    /// Сформировать имя документа для отчета.
    /// </summary>
    /// <param name="document">Документ.</param>
    /// <param name="version">Номер версии.</param>
    /// <param name="withHyperlink">Строка с гиперссылкой.</param>
    /// <returns>Имя документа в формате: Имя (ИД: 1, Версия 1/Без версии).</returns>
    [Public]
    public static string FormatDocumentNameForReport(Content.IElectronicDocument document, int version, bool withHyperlink)
    {
      var nonBreakingSpace = Convert.ToChar(160);
      
      // При использовании тегов к строке добавляются пробелы с обеих сторон, поэтому не ставится пробел в конечной строке.
      var documentId = withHyperlink
        ? string.Format(@"<font color=""blue""><u>{0}</u></font>", document.Id)
        : string.Format(@"{0}{1}", nonBreakingSpace, document.Id.ToString());
      var documentName = document.DisplayValue.Trim();
      var versionNumber = document.HasVersions && version < 1 ? document.LastVersion.Number : version;
      var documentVersion = versionNumber > 0
        ? string.Format("{1}{0}{2}", nonBreakingSpace, Docflow.Resources.Version, versionNumber)
        : Docflow.Resources.WithoutVersion;
      return string.Format("{1}{0}({2}:{3},{0}{4})",
                           nonBreakingSpace,
                           documentName,
                           Docflow.Resources.Id,
                           documentId,
                           documentVersion);
    }

    /// <summary>
    /// Получить дату со временем, день без времени вернет конец дня.
    /// </summary>
    /// <param name="date">Исходное время.</param>
    /// <param name="user">Пользователь.</param>
    /// <returns>Дата со временем.</returns>
    [Public]
    public static DateTime GetDateWithTime(DateTime date, IUser user)
    {
      return !date.HasTime() ? date.EndOfDay().FromUserTime(user) : date;
    }
    
    /// <summary>
    /// Получить дату со временем, день без времени вернет конец дня.
    /// </summary>
    /// <param name="date">Исходное время.</param>
    /// <returns>Дата со временем.</returns>
    [Public]
    public static DateTime GetDateWithTime(DateTime date)
    {
      return !date.HasTime() ? date.ToUserTime().EndOfDay().FromUserTime() : date;
    }

    /// <summary>
    /// Получить дату с припиской UTC.
    /// </summary>
    /// <param name="date">Дата.</param>
    /// <returns>Строковое представление даты с UTC.</returns>
    [Public]
    public static string GetDateWithUTCLabel(DateTime date)
    {
      var utcOffset = (Calendar.UserNow.Hour - Calendar.Now.Hour) + Calendar.UtcOffset.TotalHours;
      var utcOffsetLabel = utcOffset >= 0 ? "+" + utcOffset.ToString() : utcOffset.ToString();
      return string.Format("{0:g} (UTC{1})", date, utcOffsetLabel);
    }
    
    /// <summary>
    /// Преобразовать имя тенанта в строку для подстановки в ШК документа.
    /// </summary>
    /// <param name="tenant">Имя тенанта.</param>
    /// <returns>Идентификатор для подстановки в ШК.</returns>
    [Public]
    public static string FormatTenantIdForBarcode(string tenant)
    {
      return string.Format("{0, 10}", tenant).Substring(0, 10);
    }
    
    /// <summary>
    /// Создать отчет Журнал выгрузки документов из архива.
    /// </summary>
    /// <param name="objs">Структура данных для формирования отчета.</param>
    /// <param name="dateTimeNow">Дата и время формирования отчета.</param>
    /// <returns>Отчет.</returns>
    public static Sungero.FinancialArchive.IFinArchiveExportReport GetFinArchiveExportReport(List<Structures.Module.ExportedDocument> objs, DateTime dateTimeNow)
    {
      if (objs.Any())
      {
        var faultedDocuments = objs.Where(d => d.IsFaulted).Select(d => d.Id);
        var addendaFaulted = objs.Where(d => !d.IsFaulted && d.IsAddendum && d.LeadDocumentId != null && faultedDocuments.Contains(d.LeadDocumentId.Value));
        foreach (var addendum in addendaFaulted)
        {
          addendum.IsFaulted = true;
          addendum.Error = Resources.ExportDialog_Error_LeadDocumentNoVersion;
        }
      }

      var report = FinancialArchive.Reports.GetFinArchiveExportReport();
      report.CurrentTime = dateTimeNow;
      report.Exported = objs.Count(d => !d.IsFaulted);
      report.NotExported = objs.Count(d => d.IsFaulted);
      report.ReportSessionId = Functions.Module.Remote.GenerateFinArchiveExportReport(objs, ".");
      
      return report;
    }
    
  }
}