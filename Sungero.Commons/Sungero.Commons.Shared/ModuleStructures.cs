﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace Sungero.Commons.Structures.Module
{
  #region Интеллектуальная обработка
  
  /// <summary>
  /// Факт.
  /// </summary>
  [Public]
  partial class ArioFact
  {
    // ИД факта в Арио.
    public int Id { get; set; }
    
    // Название факта.
    public string Name { get; set; }
    
    // Список полей.
    public List<Sungero.Commons.Structures.Module.IArioFactField> Fields { get; set; }
  }
  
  /// <summary>
  /// Поле факта.
  /// </summary>
  [Public]
  partial class ArioFactField
  {
    // ИД поля в Арио.
    public int Id { get; set; }
    
    // Название поля.
    public string Name { get; set; }
    
    // Значение поля.
    public string Value { get; set; }
    
    // Вероятность.
    public double Probability { get; set; }
  }
  #endregion
}