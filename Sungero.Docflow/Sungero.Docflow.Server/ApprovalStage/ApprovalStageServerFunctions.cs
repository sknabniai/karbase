﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Company;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow.ApprovalStage;

namespace Sungero.Docflow.Server
{
  
  partial class ApprovalStageFunctions
  {
    /// <summary>
    /// Создание этапа.
    /// </summary>
    /// <returns>Новый этап.</returns>
    [Remote]
    public static IApprovalStage CreateStage()
    {
      return ApprovalStages.Create();
    }
    
    /// <summary>
    /// Удалить этап.
    /// </summary>
    /// <param name="stage">Этап.</param>
    [Remote]
    public static void DeleteStage(IApprovalStage stage)
    {
      if (stage != null)
        ApprovalStages.Delete(stage);
    }
    
    /// <summary>
    /// Определить исполнителя этапа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Исполнитель.</returns>
    /// <remarks>Для этапов с вычислением исполнителя.</remarks>
    // TODO Функция дублирует следующую за ней. Разобраться почему.
    [Remote(IsPure = true), Public]
    public static IEmployee GetRemoteStagePerformer(IApprovalTask task, IApprovalStage stage)
    {
      return GetStagePerformer(task, stage);
    }
    
    /// <summary>
    /// Определить исполнителя этапа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Исполнитель.</returns>
    /// <remarks>Для этапов с вычислением исполнителя.</remarks>
    [Remote, Public]
    public static IEmployee GetStagePerformer(IApprovalTask task, IApprovalStage stage)
    {
      return GetStagePerformer(task, stage, null, null);
    }
    
    /// <summary>
    /// Определить исполнителя этапа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <param name="signatory">Подписывающий.</param>
    /// <param name="addressee">Адресат.</param>
    /// <returns>Исполнитель.</returns>
    /// <remarks>Для этапов с вычислением исполнителя.</remarks>
    [Public]
    public static IEmployee GetStagePerformer(IApprovalTask task, IApprovalStage stage, IEmployee signatory, IEmployee addressee)
    {
      if (stage.AssigneeType == Docflow.ApprovalStage.AssigneeType.Role)
        return Functions.ApprovalRoleBase.GetRolePerformer(stage.ApprovalRole, task);
      else
        return Functions.ApprovalRuleBase.GetEmployeeByAssignee(stage.Assignee);
    }
    
    /// <summary>
    /// Определить порядковый номер этапа определенного типа в правиле.
    /// </summary>
    /// <param name="task">Задача согласования.</param>
    /// <param name="type">Тип этапа.</param>
    /// <param name="stages">Этапы.</param>
    /// <returns>Первое вхождение этапа определенного типа.</returns>
    public static int GetIndexOfStage(IApprovalTask task, Enumeration type, List<Structures.Module.DefinedApprovalStageLite> stages)
    {
      var stage = stages.Where(s => s.Stage.StageType == type).FirstOrDefault();
      return stages.IndexOf(stage);
    }
    
    /// <summary>
    /// Получить исполнителей этапа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <returns>Исполнители.</returns>
    /// <remarks>Для этапов с вычислением нескольких исполнителей.</remarks>
    [Remote(IsPure = true), Public]
    public static List<IEmployee> GetStagePerformers(IApprovalTask task, IApprovalStage stage)
    {
      return GetStagePerformers(task, stage, null);
    }
    
    /// <summary>
    /// Получить исполнителей этапа.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="stage">Этап.</param>
    /// <param name="additionalApprovers">Дополнительные согласующие.</param>
    /// <returns>Список исполнителей.</returns>
    [Remote(IsPure = true), Public]
    public static List<IEmployee> GetStagePerformers(IApprovalTask task,
                                                     IApprovalStage stage,
                                                     List<Sungero.CoreEntities.IRecipient> additionalApprovers)
    {
      var recipients = Functions.ApprovalStage.GetStageRecipients(stage, task, additionalApprovers);
      var performers = Docflow.Functions.Module.GetEmployeesFromRecipients(recipients);
      return performers.Distinct().ToList();
    }
    
    /// <summary>
    /// Получить исполнителей этапа без раскрытия групп и ролей.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <returns>Исполнители.</returns>
    [Remote(IsPure = true), Public]
    public virtual List<IRecipient> GetStageRecipients(IApprovalTask task)
    {
      return this.GetStageRecipients(task, null);
    }
    
    /// <summary>
    /// Получить исполнителей этапа без раскрытия групп и ролей.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="additionalApprovers">Доп.согласующие.</param>
    /// <returns>Исполнители.</returns>
    [Remote(IsPure = true), Public]
    public virtual List<IRecipient> GetStageRecipients(IApprovalTask task, List<Sungero.CoreEntities.IRecipient> additionalApprovers)
    {
      var recipients = new List<IRecipient>();
      
      if (_obj.StageType == StageType.Approvers || _obj.StageType == StageType.Notice || _obj.StageType == StageType.SimpleAgr || _obj.StageType == StageType.CheckReturn)
        // Сотрудники/группы.
        if (_obj.Recipients.Any())
          recipients.AddRange(_obj.Recipients
                              .Where(rec => rec.Recipient != null)
                              .Select(rec => rec.Recipient)
                              .ToList());
      
      // Роли согласования.
      if (_obj.ApprovalRoles.Any())
        recipients.AddRange(_obj.ApprovalRoles
                            .Where(r => r.ApprovalRole != null && r.ApprovalRole.Type != Docflow.ApprovalRoleBase.Type.Approvers)
                            .Select(r => Functions.ApprovalRoleBase.GetRolePerformer(r.ApprovalRole, task))
                            .Where(r => r != null)
                            .ToList());
      
      // Обработать роль "Согласующие" отдельно, так как она множественная.
      if (_obj.ApprovalRoles.Any(r => r.ApprovalRole.Type == Docflow.ApprovalRoleBase.Type.Approvers))
      {
        var role = _obj.ApprovalRoles.Where(r => r.ApprovalRole != null && r.ApprovalRole.Type == Docflow.ApprovalRoleBase.Type.Approvers)
          .Select(r => ApprovalRoles.As(r.ApprovalRole))
          .Where(r => r != null)
          .FirstOrDefault();
        recipients.AddRange(Functions.ApprovalRole.GetApproversRolePerformers(role, task, additionalApprovers));
      }
      
      // Для типа этапа "Контроль возврата" добавить исполнителем инициатора, если других исполнителей нет.
      if (_obj.StageType == StageType.CheckReturn)
      {
        var performers = Functions.Module.GetEmployeesFromRecipients(recipients);
        if (!performers.Any())
          recipients.Add(task.Author);
      }
      
      // Для согласующих дополнительно нужны те, кому переадресовали (или кого добавили в процессе согласования).
      if (_obj.StageType == StageType.Approvers)
      {
        var assignments = ApprovalAssignments.GetAll()
          .Where(a => Equals(a.Task, task) && Equals(a.TaskStartId, task.StartId) && Equals(a.Stage, _obj))
          .ToList();
        foreach (var assignment in assignments)
        {
          if (assignment.ForwardedTo == null)
            continue;
          
          recipients.AddRange(assignment.ForwardedTo);
        }
      }
      
      // Согласование с доп. согласующими.
      if (_obj.StageType == StageType.Approvers && _obj.AllowAdditionalApprovers == true)
      {
        if (additionalApprovers != null)
          recipients.AddRange(additionalApprovers);
        else
          recipients.AddRange(task.AddApproversExpanded
                              .Where(a => a.Approver != null)
                              .Select(a => a.Approver)
                              .ToList());
        
        if (task.Status != Docflow.ApprovalTask.Status.Draft)
        {
          var assignments = ApprovalAssignments.GetAll()
            .Where(a => Equals(a.Task, task) && Equals(a.TaskStartId, task.StartId) && Equals(a.Stage, _obj) && Equals(a.Status, Workflow.Assignment.Status.InProcess))
            .ToList();
          foreach (var assignment in assignments)
          {
            if (assignment.ForwardedTo == null)
              continue;

            recipients.AddRange(assignment.ForwardedTo);
          }
        }
      }
      
      return recipients.Distinct().ToList();
    }
    
    /// <summary>
    /// Проверить, не используется ли этап в правилах.
    /// </summary>
    /// <param name="stage">Проверяемый этап.</param>
    /// <returns>True, если используется, false, если нет.</returns>
    [Remote(IsPure = true), Public]
    public static bool HasRules(IApprovalStage stage)
    {
      return ApprovalRuleBases.GetAll(r => r.Stages.Any(s => s.Stage == stage)).Any();
    }
    
    /// <summary>
    /// Получить список правил согласования с текущим этапом согласования.
    /// </summary>
    /// <returns>Список правил согласования.</returns>
    [Remote]
    public IQueryable<IApprovalRuleBase> GetApprovalRules()
    {
      return ApprovalRuleBases.GetAll(r => r.Stages.Any(s => Equals(s.Stage, _obj)));
    }
    
    /// <summary>
    /// Получить подтверждающего подписание.
    /// </summary>
    /// <param name="signatory">Подписывающий.</param>
    /// <param name="task">Задача на согласование.</param>
    /// <returns>Подтверждающий подписание.</returns>
    public Company.IEmployee GetConfirmByForSignatory(Company.IEmployee signatory, IApprovalTask task)
    {
      if (_obj.IsConfirmSigning != true)
        return null;
      
      var stage = Functions.ApprovalTask.GetStages(task).Stages.Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Sign).Select(s => s.Stage).FirstOrDefault();
      
      IEmployee performer = null;
      
      if (stage != null)
        performer = Functions.ApprovalStage.GetStagePerformer(task, stage);
      
      return Equals(performer, signatory) ? null : performer;
    }

    /// <summary>
    /// Получить вносящего результат рассмотрения.
    /// </summary>
    /// <param name="addressee">Адресат.</param>
    /// <param name="task">Задача на согласование.</param>
    /// <returns>Вносящий результат рассмотрения за адресата.</returns>
    public Company.IEmployee GetAddresseeAssistantForResultSubmission(Company.IEmployee addressee, IApprovalTask task)
    {
      if (_obj.IsResultSubmission != true)
        return null;
      var stage = Functions.ApprovalTask.GetStages(task).Stages.Where(s => s.Stage.StageType == Docflow.ApprovalStage.StageType.Review).Select(s => s.Stage).FirstOrDefault();
      
      IEmployee performer = null;
      
      if (stage != null)
        performer = Functions.ApprovalStage.GetStagePerformer(task, stage);
      
      return Equals(performer, addressee) ? null : performer;
    }
    
    /// <summary>
    /// Получить список правил, в которых не могут быть использованы выбранные роли.
    /// </summary>
    /// <returns>Список правил.</returns>
    [Remote]
    public List<IApprovalRuleBase> GetRulesWithImpossibleRoles()
    {
      var roles = _obj.ApprovalRoles.Where(r => r != null).Select(r => r.ApprovalRole.Type).ToList();
      if (_obj.ApprovalRole != null)
        roles.Add(_obj.ApprovalRole.Type);
      if (_obj.ReworkApprovalRole != null)
        roles.Add(_obj.ReworkApprovalRole.Type);
      
      if (roles.Any())
      {
        var rules = Functions.ApprovalStage.GetApprovalRules(_obj).Where(r => r.Status == Sungero.CoreEntities.DatabookEntry.Status.Active).ToList();
        
        // Проверка на адресата, подписывающего и их помощников.
        var impossibleRules = rules.Where(x => Functions.ApprovalRuleBase.GetAllStagesVariants(x).AllSteps.Any(y => Functions.ApprovalRuleBase.CheckImpossibleRoles(x, y, false, roles).Any())).ToList();
        
        // Проверка ответственного за договор и руководителя ответственного за договор.
        if (roles.Any(x => Equals(x, Docflow.ApprovalRoleBase.Type.ContractResp) || Equals(x, Docflow.ApprovalRoleBase.Type.ContRespManager)))
        {
          foreach (var role in rules.Where(x => ApprovalRules.Is(x)))
            impossibleRules.Add(role);
        }
        
        return impossibleRules;
      }
      
      return new List<IApprovalRuleBase>();
    }
    
    /// <summary>
    /// Проверка прав регистратора.
    /// </summary>
    /// <param name="registerStage">Этап.</param>
    /// <returns>True - если есть права на регистрацию хотя бы одного документопотока.</returns>
    [Remote(IsPure = true)]
    public static bool ClerkCanRegister(IApprovalStage registerStage)
    {
      var canRegister = true;
      var clerk = Functions.ApprovalRuleBase.GetEmployeeByAssignee(registerStage.Assignee);
      if (registerStage.StageType == StageType.Register && clerk != null)
      {
        canRegister = IncomingDocumentBases.AccessRights.CanRegister(clerk) || OutgoingDocumentBases.AccessRights.CanRegister(clerk) ||
          InternalDocumentBases.AccessRights.CanRegister(clerk) || ContractualDocumentBases.AccessRights.CanRegister(clerk);
      }
      return canRegister;
    }
    
    /// <summary>
    /// Получить настройки этапа.
    /// </summary>
    /// <returns>Строка с перечнем настроек.</returns>
    public virtual string GetStageSettings()
    {
      var settings = new List<string>();
      var resources = Reports.Resources.ApprovalRuleCardReport;
      
      // Согласование.
      if (_obj.StageType == StageType.Approvers || _obj.StageType == StageType.Manager)
      {
        if (_obj.NeedStrongSign == true)
          settings.Add(resources.NeedStrongSign);
      }
      
      // Подписание.
      if (_obj.StageType == StageType.Sign)
      {
        if (_obj.NeedStrongSign == true)
          settings.Add(resources.NeedStrongSign);
        if (_obj.IsConfirmSigning == true)
          settings.Add(resources.ConfirmSinging);
      }
      
      // Рассмотрение.
      if (_obj.StageType == StageType.Review)
      {
        if (_obj.NeedStrongSign == true)
          settings.Add(resources.NeedStrongSign);
        if (_obj.IsResultSubmission == true)
          settings.Add(resources.ReslutSubmission);
      }

      // Контроль возврата.
      if (_obj.StageType == StageType.CheckReturn)
      {
        if (_obj.StartDelayDays.HasValue && _obj.StartDelayDays.Value > 0)
          settings.Add(string.Format("{0} - {1}", resources.StartDelayDays, _obj.StartDelayDays.Value));
      }
      
      return string.Join(Environment.NewLine, settings);
    }
    
    /// <summary>
    /// Добавить в настройку ответственного за доработку.
    /// </summary>
    /// <returns>Ответственные за доработку.</returns>
    public virtual string GetReworkSettings()
    {
      var settings = new List<string>();

      // Разрешение отправки на доработку.
      if (_obj.StageType == StageType.SimpleAgr || _obj.StageType == StageType.Register ||
          _obj.StageType == StageType.Sending || _obj.StageType == StageType.Print || _obj.StageType == StageType.Execution)
      {
        if (_obj.AllowSendToRework == true)
          settings.Add(string.Format("{0}", Reports.Resources.ApprovalRuleCardReport.AllowSendToRework));
      }
      
      // Порядок доработки.
      var reworkType = _obj.ReworkType.HasValue
        ? ApprovalStages.Info.Properties.ReworkType.GetLocalizedValue(_obj.ReworkType)
        : string.Empty;
      if (!string.IsNullOrEmpty(reworkType))
        settings.Add(string.Format("{0}", reworkType.ToLower()));
      
      // Разрешение выбора ответственного за доработку.
      if (_obj.AllowChangeReworkPerformer == true)
        settings.Add(string.Format("{0}", ApprovalStages.Resources.ReportAllowChangeReworkPerformer));
      
      // Ответственный за доработку.
      if (_obj.StageType == StageType.Register || _obj.StageType == StageType.Print || _obj.StageType == StageType.Sending ||
          _obj.StageType == StageType.Execution || _obj.StageType == StageType.Approvers || _obj.StageType == StageType.Manager ||
          _obj.StageType == StageType.Sign || _obj.StageType == StageType.Review || _obj.StageType == StageType.SimpleAgr)
      {
        var reworkPerformer = string.Empty;
        if (_obj.ReworkPerformerType == ReworkPerformerType.EmployeeRole)
          reworkPerformer = GetRecipientDescription(_obj.ReworkPerformer);
        if (_obj.ReworkPerformerType == ReworkPerformerType.Author)
          reworkPerformer = ApprovalStages.Resources.ReportAuthor;
        if (_obj.ReworkPerformerType == ReworkPerformerType.ApprovalRole)
          reworkPerformer = string.Format("\"{0}\"", _obj.ReworkApprovalRole.Name);
        if (!string.IsNullOrEmpty(reworkPerformer))
          settings.Add(string.Format("{0}: {1}", Reports.Resources.ApprovalRuleCardReport.ReworkPerformer.ToString().ToLower(), reworkPerformer));
      }
      
      var separator = string.Empty;
      if (settings.Count() > 1)
      {
        separator = string.Concat(Environment.NewLine, "- ");
        settings[0] = string.Concat(separator, settings[0]);
      }
      else if (settings.Any())
      {
        settings[0] = string.Format("{0}{1}", settings[0].Substring(0, 1).ToUpper(), settings[0].Remove(0, 1));
      }
      
      return string.Join(separator, settings);
    }
    
    /// <summary>
    /// Получить представление сотрудника.
    /// </summary>
    /// <param name="employee">Сотрудник.</param>
    /// <returns>Строковое представление сотрудника.</returns>
    public static string GetEmployeePresentation(IEmployee employee)
    {
      var employeeShortName = Company.PublicFunctions.Employee.GetShortName(employee, false).Trim();
      
      return employee.JobTitle == null
        ? employeeShortName
        : string.Format("{0} {1}", employee.JobTitle.Name.Trim(), employeeShortName).Trim();
    }
    
    /// <summary>
    /// Получить представление исполнителя.
    /// </summary>
    /// <param name="recipient">Роль/сотрудник.</param>
    /// <returns>Представление.</returns>
    public static string GetRecipientDescription(IRecipient recipient)
    {
      var result = string.Empty;
      var employee = Employees.As(recipient);
      if (employee != null)
      {
        result = Functions.ApprovalStage.GetEmployeePresentation(employee);
      }
      else
      {
        var group = Sungero.CoreEntities.Groups.As(recipient);
        if (group != null)
        {
          
          var employees = Docflow.PublicFunctions.Module.Remote.GetEmployeesFromRecipientsRemote(new List<IRecipient> { recipient }).Distinct();
          List<string> employeesDescription = employees.Select(e => Functions.ApprovalStage.GetEmployeePresentation(e)).ToList();
          result = string.Join(string.Format("{0}{1}", ",", Environment.NewLine), employeesDescription);
          result = string.Format("\"{0}\" ({1})", recipient.Name, result);
        }
        else
        {
          result = string.Format("\"{0}\"", recipient.Name);
        }
      }
      
      return result;
    }
    
    /// <summary>
    /// Добвить этап в схему правила согласования.
    /// </summary>
    /// <param name="linedRoute">Схема правила.</param>
    /// <param name="prefix">Префикс перед заголовком.</param>
    /// <param name="level">Отступ от левого края: 0, 1, 2.</param>
    /// <remarks>Используется в отчете Печать правила согласования. Вынесено в этапы для перекрываемости.</remarks>
    public virtual void AddStageToRoute(List<Structures.ApprovalRuleCardReport.ConditionTableLine> linedRoute, string prefix, int level)
    {
      var tableLine = new Structures.ApprovalRuleCardReport.ConditionTableLine();
      var resources = Reports.Resources.ApprovalRuleCardReport;
      
      // Исполнители / роли. Роли выделить кавычками.
      var performers = new List<string>();
      if (_obj.Assignee != null)
        performers.Add(Functions.ApprovalStage.GetRecipientDescription(_obj.Assignee));
      if (_obj.Recipients.Any())
        performers.AddRange(_obj.Recipients.Select(a => Functions.ApprovalStage.GetRecipientDescription(a.Recipient)));
      if (_obj.ApprovalRoles.Any())
        performers.AddRange(_obj.ApprovalRoles.Select(a => string.Format("\"{0}\"", a.ApprovalRole.Name)));
      if (_obj.ApprovalRole != null)
        performers.Add(string.Format("\"{0}\"", _obj.ApprovalRole.Name));
      if (_obj.StageType == StageType.Approvers)
      {
        if (_obj.AllowAdditionalApprovers == true)
          performers.Add(resources.AllowAdditionalApprovers);
      }
      
      tableLine.Performers = string.Join(";" + Environment.NewLine, performers).Trim();
      
      var parameters = new List<string>();
      
      // Тема.
      if (!string.IsNullOrEmpty(_obj.Subject))
      {
        parameters.Add(string.Format("{0}: {1}", resources.Subject, _obj.Subject.Trim()));
      }
      
      // Старт: Друг за другом / Одновременно.
      var isApprovers = _obj.StageType == StageType.Approvers;
      var isSimpleAssignment = _obj.StageType == StageType.SimpleAgr;
      if (_obj.Sequence.HasValue && (isApprovers || isSimpleAssignment))
      {
        var sequence = ApprovalStages.Info.Properties.Sequence.GetLocalizedValue(_obj.Sequence);
        parameters.Add(string.Format("{0}: {1}", resources.Start, sequence.Trim()));
      }
      
      // Настройки.
      var result = Functions.ApprovalStage.GetStageSettings(_obj);
      if (!string.IsNullOrEmpty(result))
      {
        parameters.Add(string.Format("{0}: {1}", resources.Settings, result.Trim()));
      }
      
      // Доработка.
      var rework = Functions.ApprovalStage.GetReworkSettings(_obj);
      if (!string.IsNullOrEmpty(rework))
      {
        parameters.Add(string.Format("{0}: {1}", resources.Rework, rework));
      }

      tableLine.Parameters = string.Join(System.Environment.NewLine, parameters);
      
      // Срок.
      if (_obj.StageType != StageType.Notice)
      {
        var deadline = Functions.ApprovalStage.GetDeadlineDescription(_obj, 1, Environment.NewLine, false);
        if (deadline == string.Empty)
          deadline = "-";
        tableLine.Deadline = deadline.Trim();
      }

      // Тип этапа.
      tableLine.StageType = ApprovalStages.Info.Properties.StageType.GetLocalizedValue(_obj.StageType);
      
      var ruleId = _obj.Id.ToString();
      var hyperlink = Hyperlinks.Get(_obj);
      tableLine.RuleId = ruleId;
      tableLine.Hyperlink = hyperlink;
      tableLine.Header = ApprovalRuleCardReportServerHandlers.BreakLineAndAddPadding(_obj.Name, Constants.ApprovalRuleCardReport.StageCellWidth, level);
      tableLine.Level = level;
      tableLine.IsCondition = false;

      linedRoute.Add(tableLine);
    }
    
    /// <summary>
    /// Получить роли согласования, допустимые в качестве ответственных за доработку.
    /// </summary>
    /// <param name="withoutContractRoles">Исключить договорные роли.</param>
    /// <returns>Список ролей согласования.</returns>
    public virtual List<IApprovalRoleBase> GetSupportedApprovalRolesForRework(bool withoutContractRoles)
    {
      var roles = ApprovalRoleBases.GetAll().Where(r => r.Type != Docflow.ApprovalRole.Type.Initiator &&
                                                   r.Type != Docflow.ApprovalRole.Type.Approvers &&
                                                   r.Type != Docflow.ApprovalRole.Type.Addressee &&
                                                   r.Type != Docflow.ApprovalRole.Type.AddrAssistant &&
                                                   r.Type != Docflow.ApprovalRole.Type.Signatory &&
                                                   r.Type != Docflow.ApprovalRole.Type.SignAssistant);
      if (withoutContractRoles)
        roles = roles.Where(r => r.Type != Docflow.ApprovalRole.Type.ContractResp &&
                            r.Type != Docflow.ApprovalRole.Type.ContRespManager);
      return roles.ToList();
    }
  }
}